using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http.Shims;

namespace Cysharp.Net.Http
{
    internal class ResponseContext
    {
        private readonly RequestContext _requestContext;
        private readonly Pipe _pipe;
        private readonly TaskCompletionSource<HttpResponseMessage> _responseTask;
        private readonly HttpResponseMessage _message;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _tokenRegistration;
        private readonly object _writeLock = new object();
        private bool _completed = false;
        private Task<FlushResult>? _latestFlushTask;
        private IntPtr _deferredAck;

        internal ResponseContext(HttpRequestMessage requestMessage, RequestContext requestContext, PipeOptions? pipeOptions, CancellationToken cancellationToken)
        {
            _pipe = new Pipe(pipeOptions ?? PipeOptions.Default);
            _requestContext = requestContext;
            _responseTask = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancellationToken = cancellationToken;
            _tokenRegistration = cancellationToken.Register(static (state) =>
            {
                ((ResponseContext)state!).Cancel();
            }, this);

            _message = new HttpResponseMessage()
            {
                RequestMessage = requestMessage,
                Content = new YetAnotherHttpHttpContent(requestContext, _pipe.Reader),
                Version = HttpVersion.Version10,
            };
#if NETSTANDARD2_0
            _message.EnsureTrailingHeaders();
#endif
        }

        /// <summary>
        /// Writes response data. When the flush does not complete synchronously, <paramref name="ackHandle"/>
        /// is held as a deferred acknowledgement (<paramref name="ackDeferred"/> is <c>true</c>) and must be
        /// claimed with <see cref="TryTakeDeferredAck"/> before completing it, even if the returned task has
        /// already completed; <see cref="Cancel"/> may claim and complete it first.
        /// </summary>
        public ValueTask<FlushResult> WriteAsync(ReadOnlySpan<byte> data, nuint ackHandle, out bool ackDeferred)
        {
            ackDeferred = false;
            lock (_writeLock)
            {
                if (_completed) return default;

                WaitForLatestFlush();

                var buffer = _pipe.Writer.GetSpan(data.Length);
                data.CopyTo(buffer);
                _pipe.Writer.Advance(data.Length);

                var flush = _pipe.Writer.FlushAsync(_cancellationToken);
                if (flush.IsCompleted)
                {
                    _latestFlushTask = null;
                    return flush;
                }

                _latestFlushTask = flush.AsTask();
                _deferredAck = (IntPtr)(nint)ackHandle;
                ackDeferred = true;
                return new ValueTask<FlushResult>(_latestFlushTask);
            }
        }

        public bool TryTakeDeferredAck(nuint ackHandle)
        {
            var handle = (IntPtr)(nint)ackHandle;
            return Interlocked.CompareExchange(ref _deferredAck, IntPtr.Zero, handle) == handle;
        }

        public void SetHeader(ReadOnlySpan<byte> nameBytes, ReadOnlySpan<byte> valueBytes)
        {
            var (name, isHttpContentHeader) = Utf8Strings.HttpHeaders.FromSpan(nameBytes);
            var value = UnsafeUtilities.GetStringFromUtf8Bytes(valueBytes);
            if (isHttpContentHeader)
            {
                _message.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                _message.Headers.TryAddWithoutValidation(name, value);
            }
        }

        internal void SetVersion(YahaHttpVersion version)
        {
            _message.Version = version switch
            {
                YahaHttpVersion.Http10 => HttpVersion.Version10,
                YahaHttpVersion.Http11 => HttpVersion.Version11,
                YahaHttpVersion.Http2 => HttpVersionShim.Version20,
                _ => HttpVersionShim.Unknown,
            };
        }

        public void SetTrailer(ReadOnlySpan<byte> nameBytes, ReadOnlySpan<byte> valueBytes)
        {
            var (name, isHttpContentHeader) = Utf8Strings.HttpHeaders.FromSpan(nameBytes);
            var value = UnsafeUtilities.GetStringFromUtf8Bytes(valueBytes);

            _message.TrailingHeaders().TryAddWithoutValidation(name, value);
        }

        public void SetStatusCode(int statusCode)
        {
            _message.StatusCode = (HttpStatusCode)statusCode;
            _responseTask.TrySetResult(_message);
        }

        public void Complete()
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestContext.RequestSequence}] Response completed. (_completed={_completed})");
            lock (_writeLock)
            {
                if (_completed) return;

                WaitForLatestFlush();
                _pipe.Writer.Complete();
                _completed = true;
            }
            _tokenRegistration.Dispose();
        }

        public void CompleteAsFailed(string errorMessage, uint h2ErrorCode)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestContext.RequestSequence}] Response completed with failure ({errorMessage}) (0x{h2ErrorCode:x})");

            lock (_writeLock)
            {
                if (_completed) return;

                Exception ex = new IOException(errorMessage);
                if (h2ErrorCode != 0)
                {
#if NET7_0_OR_GREATER
                    ex = new HttpProtocolException(h2ErrorCode, $"The HTTP/2 server closed the connection or reset the stream. HTTP/2 error code '{Http2ErrorCode.ToName(h2ErrorCode)}' (0x{h2ErrorCode:x}).", ex);
#else
                    ex = new Http2StreamException($"The HTTP/2 server closed the connection or reset the stream. HTTP/2 error code '{Http2ErrorCode.ToName(h2ErrorCode)}' (0x{h2ErrorCode:x}).", ex);
#endif
                }

#if NET5_0_OR_GREATER
                ExceptionDispatchInfo.SetCurrentStackTrace(ex);
#else
                try
                {
                    throw ex;
                }
                catch (Exception e)
                {
                    ex = e;
                }
#endif
                _responseTask.TrySetException(ex);
                WaitForLatestFlush();
                _pipe.Writer.Complete(ex);
                _completed = true;
            }

            _tokenRegistration.Dispose();
        }

        public void Cancel()
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestContext.RequestSequence}] Response was cancelled");

            // Unblock a full pipe before taking _writeLock: a callback thread can be
            // blocked in WaitForLatestFlush() while holding it.
            // Covered by Dispose_UnblocksUnreadResponseBody.
            _pipe.Writer.CancelPendingFlush();
            lock (_writeLock)
            {
                if (_completed) return;

                _requestContext.TryAbort();
                _responseTask.TrySetCanceled(_cancellationToken);
                // Do not wait for the latest flush task: its continuation runs on
                // the PipeOptions writer scheduler, which may be the thread calling
                // Cancel (e.g. a main-thread scheduler during Dispose). No write can
                // start while _writeLock is held, so cancelling here leaves no
                // pending flush when the writer is completed.
                _pipe.Writer.CancelPendingFlush();
                _pipe.Writer.Complete(new OperationCanceledException(_cancellationToken));
                _completed = true;
            }

            // Acknowledge a deferred OnReceive without waiting for its flush
            // continuation; Dispose waits for every acknowledgement to be released.
            // Covered by Dispose_DoesNotWaitForFlushContinuationOnDisposingThread.
            var deferredAck = Interlocked.Exchange(ref _deferredAck, IntPtr.Zero);
            if (deferredAck != IntPtr.Zero)
            {
                NativeHttpHandlerCore.CompleteTask((nuint)(nint)deferredAck, "The response was cancelled.");
            }

            _tokenRegistration.Dispose();
        }

        public async Task<HttpResponseMessage> GetResponseAsync()
        {
            try
            {
                return await _responseTask.Task.ConfigureAwait(false);
            }
#if UNITY_2021_1_OR_NEWER
            // NOTE: .NET's HttpClient will unwrap OperationCanceledException if HttpRequestException is thrown, including OperationCanceledException.
            //        However, Unity's Mono does not do this, so you will need to unwrap it manually.
            catch (OperationCanceledException)
            {
                throw;
            }
#endif
            catch (Exception e)
            {
                throw new HttpRequestException(e.Message, e);
            }
        }

        private void WaitForLatestFlush()
        {
            // PipeWriter is not thread-safe, so we need to wait for the latest flush task to complete before writing to the pipe.

            if (_latestFlushTask is { IsCompleted: false } latestFlushTask)
            {
                try
                {
                    latestFlushTask.Wait();
                }
                catch (Exception)
                {
                    // It is safe to ignore an exception thrown by the latest flush task because it will be caught by NativeHttpHandlerCore.OnReceive().
                }
            }
        }

        internal static class Http2ErrorCode
        {
            // https://github.com/dotnet/aspnetcore/blob/release/8.0/src/Shared/ServerInfrastructure/Http2/Http2ErrorCode.cs
            // https://github.com/dotnet/runtime/blob/release/8.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpProtocolException.cs#L63
            public static string ToName(uint code) => code switch
            {
                0x0 => "NO_ERROR",
                0x1 => "PROTOCOL_ERROR",
                0x2 => "INTERNAL_ERROR",
                0x3 => "FLOW_CONTROL_ERROR",
                0x4 => "SETTINGS_TIMEOUT",
                0x5 => "STREAM_CLOSED",
                0x6 => "FRAME_SIZE_ERROR",
                0x7 => "REFUSED_STREAM",
                0x8 => "CANCEL",
                0x9 => "COMPRESSION_ERROR",
                0xa => "CONNECT_ERROR",
                0xb => "ENHANCE_YOUR_CALM",
                0xc => "INADEQUATE_SECURITY",
                0xd => "HTTP_1_1_REQUIRED",
                _ => "(unknown error)",
            };
        }
    }
}
