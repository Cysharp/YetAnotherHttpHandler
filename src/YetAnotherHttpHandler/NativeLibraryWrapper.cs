#if NET5_0_OR_GREATER
#define USE_FUNCTION_POINTER
#endif

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_2019_1_OR_NEWER
using AOT;
#endif

namespace Cysharp.Net.Http
{
    internal class NativeLibraryWrapper : IDisposable
    {
        private unsafe YahaNativeContext* _ctx;
        private static ConcurrentDictionary<int, RequestContext> _inflightRequests = new ConcurrentDictionary<int, RequestContext>();
        private static int _requestSequence = 0;

        public unsafe NativeLibraryWrapper(NativeClientSettings settings)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_init_runtime");
#if USE_FUNCTION_POINTER
            _ctx = NativeMethodsFuncPtr.yaha_init_runtime(&OnStatusCodeAndHeaderReceive, &OnReceive, &OnComplete);
#else
            _ctx = NativeMethods.yaha_init_runtime(OnStatusCodeAndHeaderReceive, OnReceive, OnComplete);
#endif

            if (settings.SkipCertificateVerification is {} skipCertificateVerification)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.SkipCertificateVerification)}' = {skipCertificateVerification}");
                NativeMethods.yaha_client_config_skip_certificate_verification(_ctx, skipCertificateVerification);
            }
            if (settings.Http2Only is {} http2Only)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2Only)}' = {http2Only}");
                NativeMethods.yaha_client_config_http2_only(_ctx, http2Only);
            }
            if (settings.Http2InitialStreamWindowSize is {} http2InitialStreamWindowSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2InitialStreamWindowSize)}' = {http2InitialStreamWindowSize}");
                NativeMethods.yaha_client_config_http2_initial_stream_window_size(_ctx, http2InitialStreamWindowSize);
            }
            if (settings.Http2InitialConnectionWindowSize is {} http2InitialConnectionWindowSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2InitialConnectionWindowSize)}' = {http2InitialConnectionWindowSize}");
                NativeMethods.yaha_client_config_http2_initial_connection_window_size(_ctx, http2InitialConnectionWindowSize);
            }
            if (settings.Http2AdaptiveWindow is {} http2AdaptiveWindow)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2AdaptiveWindow)}' = {http2AdaptiveWindow}");
                NativeMethods.yaha_client_config_http2_adaptive_window(_ctx, http2AdaptiveWindow);
            }
            if (settings.Http2MaxFrameSize is {} http2MaxFrameSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxFrameSize)}' = {http2MaxFrameSize}");
                NativeMethods.yaha_client_config_http2_max_frame_size(_ctx, http2MaxFrameSize);
            }
            if (settings.Http2KeepAliveInterval is {} http2KeepAliveInterval)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveInterval)}' = {http2KeepAliveInterval}");
                NativeMethods.yaha_client_config_http2_keep_alive_interval(_ctx, (ulong)http2KeepAliveInterval.TotalMilliseconds);
            }
            if (settings.Http2KeepAliveTimeout is {} http2KeepAliveTimeout)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveTimeout)}' = {http2KeepAliveTimeout}");
                NativeMethods.yaha_client_config_http2_keep_alive_timeout(_ctx, (ulong)http2KeepAliveTimeout.TotalMilliseconds);
            }
            if (settings.Http2KeepAliveWhileIdle is {} http2KeepAliveWhileIdle)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveWhileIdle)}' = {http2KeepAliveWhileIdle}");
                NativeMethods.yaha_client_config_http2_keep_alive_while_idle(_ctx, http2KeepAliveWhileIdle);
            }
            if (settings.Http2MaxConcurrentResetStreams is {} http2MaxConcurrentResetStreams)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxConcurrentResetStreams)}' = {http2MaxConcurrentResetStreams}");
                NativeMethods.yaha_client_config_http2_max_concurrent_reset_streams(_ctx, (nuint)http2MaxConcurrentResetStreams);
            }
            if (settings.Http2MaxSendBufferSize is {} http2MaxSendBufferSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxSendBufferSize)}' = {http2MaxSendBufferSize}");
                NativeMethods.yaha_client_config_http2_max_send_buf_size(_ctx, (nuint)http2MaxSendBufferSize);
            }

            NativeMethods.yaha_build_client(_ctx);

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"NativeLibraryWrapper created");
        }

        public unsafe RequestContext Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.RequestUri is null)
            {
                throw new InvalidOperationException("A request URI cannot be null.");
            }

            var requestSequence = Interlocked.Increment(ref _requestSequence);
            var reqCtx = NativeMethods.yaha_request_new(_ctx, requestSequence);

            // Set request headers
            var headers = request.Content is null
                ? request.Headers
                : request.Headers.Concat(request.Content.Headers);
            foreach (var header in headers)
            {
                var keyBytes = Encoding.UTF8.GetBytes(header.Key);
                var valueBytes = Encoding.UTF8.GetBytes(string.Join(",", header.Value));

                fixed (byte* pKey =  keyBytes)
                fixed (byte* pValue = valueBytes)
                {
                    var bufKey = new StringBuffer(pKey, keyBytes.Length);
                    var bufValue = new StringBuffer(pValue, valueBytes.Length);
                    VerifyPointer(_ctx, reqCtx);
                    ThrowIfFailed(NativeMethods.yaha_request_set_header(_ctx, reqCtx, &bufKey, &bufValue));
                }
            }

            // Set HTTP method
            {
                var strBytes = Encoding.UTF8.GetBytes(request.Method.ToString());
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    VerifyPointer(_ctx, reqCtx);
                    ThrowIfFailed(NativeMethods.yaha_request_set_method(_ctx, reqCtx, &buf));
                }
            }

            // Set URI
            {
                var strBytes = Encoding.UTF8.GetBytes(request.RequestUri.ToString());
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    VerifyPointer(_ctx, reqCtx);
                    ThrowIfFailed(NativeMethods.yaha_request_set_uri(_ctx, reqCtx, &buf));
                }
            }
            
            // Set HTTP version
            var version = request.Version switch
            {
                var v when v == HttpVersion.Version10 => YahaHttpVersion.Http10,
                var v when v == HttpVersion.Version11 => YahaHttpVersion.Http11,
                var v when v == HttpVersionShim.Version20 => YahaHttpVersion.Http2,
                _ => throw new NotSupportedException($"Unsupported HTTP version '{request.Version}'"),
            };
            NativeMethods.yaha_request_set_version(_ctx, reqCtx, version);

            // Prepare body channel
            NativeMethods.yaha_request_set_has_body(_ctx, reqCtx, request.Content != null);

            // Begin request
            var requestContextManaged = new RequestContext(_ctx, reqCtx, request, requestSequence, cancellationToken);
            _inflightRequests[requestSequence] = requestContextManaged;

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{requestSequence}] Begin HTTP request to the server.");
            VerifyPointer(_ctx, reqCtx);
            ThrowIfFailed(NativeMethods.yaha_request_begin(_ctx, reqCtx));
            requestContextManaged.Start(); // NOTE: ReadRequestLoop must be started after `request_begin`.

            return requestContextManaged;
        }

        [Conditional("__VERIFY_POINTER")]
        private static unsafe void VerifyPointer(YahaNativeContext* ctx, YahaNativeRequestContext* reqCtx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (reqCtx == null) throw new ArgumentNullException(nameof(reqCtx));
        }

        private static unsafe void ThrowIfFailed(bool result)
        {
            if (!result)
            {
                var buf = NativeMethods.yaha_get_last_error();
                if (buf != null)
                {
                    try
                    {
                        throw new InvalidOperationException(UnsafeUtilities.GetStringFromUtf8Bytes(buf->AsSpan()));
                    }
                    finally
                    {
                        NativeMethods.yaha_free_byte_buffer(buf);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unexpected error occurred.");
                }
            }
        }
        

        public class RequestContext : IDisposable
        {
            private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);
            private readonly CancellationToken _cancellationToken;
            private readonly int _requestSequence;

            internal unsafe YahaNativeContext* _ctx;
            internal unsafe YahaNativeRequestContext* _requestContext;

            private bool _requestBodyCompleted;
            private Task? _readRequestTask;
            private Response _response;

            public int RequestSequence => _requestSequence;
            public Response Response => _response;
            public PipeWriter Writer => _pipe.Writer;

            internal unsafe RequestContext(YahaNativeContext* ctx, YahaNativeRequestContext* requestContext, HttpRequestMessage requestMessage, int requestSequence, CancellationToken cancellationToken)
            {
                _ctx = ctx;
                _requestContext = requestContext;
                _response = new Response(requestMessage, this, cancellationToken);
                _readRequestTask = default;
                _requestSequence = requestSequence;
                _cancellationToken = cancellationToken;
            }

            internal void Start()
            {
                _readRequestTask = RunReadRequestLoopAsync(_cancellationToken);
            }

            private async Task RunReadRequestLoopAsync(CancellationToken cancellationToken)
            {
                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                        if (result.Buffer.Length > 0)
                        {
                            var buffer = ArrayPool<byte>.Shared.Rent((int)result.Buffer.Length);
                            try
                            {
                                result.Buffer.CopyTo(buffer);
                                Write(buffer.AsSpan(0, (int)result.Buffer.Length));
                                _pipe.Reader.AdvanceTo(result.Buffer.End);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }

                        if (result.IsCompleted || result.IsCanceled) break;
                    }

                    TryComplete();
                }
                catch (Exception e)
                {
                    TryComplete(e);
                }

                unsafe void Write(Span<byte> data)
                {
                    if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}] Sending the request body: Length={data.Length}");
                    VerifyPointer(_ctx, _requestContext);

                    while (!_requestBodyCompleted)
                    {
                        // If the internal buffer is full, yaha_request_write_body returns false. We need to wait until ready to send bytes again.
                        if (NativeMethods.yaha_request_write_body(_ctx, _requestContext, (byte*)Unsafe.AsPointer(ref data.GetPinnableReference()), (UIntPtr)data.Length))
                        {
                            break;
                        }
                        if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}] Send buffer is full.");

                        // TODO:
                        Thread.Sleep(10);
                    }
                }
            }

            public unsafe void TryComplete(Exception? exception = default)
            {
                if (_ctx == null || _requestContext == null)
                {
                    // The request has already completed. We need to nothing to do at here.
                    return;
                }

                if (_requestBodyCompleted)
                {
                    return;
                }

                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}] Complete sending the request body{(exception is null ? string.Empty : "; Exception=" + exception.Message)}");

                VerifyPointer(_ctx, _requestContext);
                ThrowIfFailed(NativeMethods.yaha_request_complete_body(_ctx, _requestContext));
                
                _pipe.Reader.Complete(exception);

                _requestBodyCompleted = true;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            ~RequestContext()
            {
                Dispose(false);
            }

            private unsafe void Dispose(bool disposing)
            {
                if (_requestContext != null)
                {
                    if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}] Disposing RequestContext");
                    VerifyPointer(_ctx, _requestContext);
                    ThrowIfFailed(NativeMethods.yaha_request_destroy(_ctx, _requestContext));
                    _requestContext = null;
                }

                if (disposing)
                {
                    _ctx = null;
                }
            }
        }

        public class Response
        {
            private readonly int _requestSequence;
            private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);
            private readonly TaskCompletionSource<HttpResponseMessage> _responseTask;
            private readonly HttpResponseMessage _message;
            private readonly CancellationToken _cancellationToken;
            private readonly CancellationTokenRegistration _tokenRegistration;

            public PipeReader Reader => _pipe.Reader;

            internal Response(HttpRequestMessage requestMessage, RequestContext requestContext, CancellationToken cancellationToken)
            {
                _responseTask = new TaskCompletionSource<HttpResponseMessage>();
                _requestSequence = requestContext.RequestSequence;
                _cancellationToken = cancellationToken;
                _tokenRegistration = cancellationToken.Register((state) =>
                {
                    ((Response)state!).Cancel();
                }, this);

                _message = new HttpResponseMessage()
                {
                    RequestMessage = requestMessage,
                    Content = new YetAnotherHttpHttpContent(Reader, requestContext),
                    Version = HttpVersion.Version10,
                };
#if NETSTANDARD2_0
                _message.EnsureTrailingHeaders();
#endif
            }

            public void Write(ReadOnlySpan<byte> data)
            {
                var buffer = _pipe.Writer.GetSpan(data.Length);
                data.CopyTo(buffer);
                _pipe.Writer.Advance(data.Length);
                var t = _pipe.Writer.FlushAsync();
                if (!t.IsCompleted)
                {
                    t.AsTask().GetAwaiter().GetResult();
                }
            }

            public void SetHeader(string name, string value)
            {
                if (!_message.Headers.TryAddWithoutValidation(name, value))
                {
                    _message.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }
            
            public void SetHeader(ReadOnlySpan<byte> nameBytes, ReadOnlySpan<byte> valueBytes)
            {
                var name = UnsafeUtilities.GetStringFromUtf8Bytes(nameBytes);
                var value = UnsafeUtilities.GetStringFromUtf8Bytes(valueBytes);

                if (!_message.Headers.TryAddWithoutValidation(name, value))
                {
                    _message.Content.Headers.TryAddWithoutValidation(name, value);
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

            public void SetTrailer(string name, string value)
            {
                _message.TrailingHeaders().TryAddWithoutValidation(name, value);
            }

            public void SetTrailer(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
            {
                _message.TrailingHeaders().TryAddWithoutValidation(UnsafeUtilities.GetStringFromUtf8Bytes(name), UnsafeUtilities.GetStringFromUtf8Bytes(value));
            }

            public void SetStatusCode(int statusCode)
            {
                _message.StatusCode = (HttpStatusCode)statusCode;
                _responseTask.TrySetResult(_message);
            }

            public void Complete()
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}] Response completed.");
                _pipe.Writer.Complete();
            }

            public void CompleteAsFailed(string errorMessage)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}] Response completed with failure ({errorMessage})");

                var ex = new HttpRequestException(errorMessage);
#if NET5_0_OR_GREATER
                ExceptionDispatchInfo.SetCurrentStackTrace(ex);
#else
                try
                {
                    throw new HttpRequestException(errorMessage);
                }
                catch (HttpRequestException e)
                {
                    ex = e;
                }
#endif
                _responseTask.TrySetException(ex);
                _pipe.Writer.Complete(ex);

            }

            public void Cancel()
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}] Response was cancelled");

                _responseTask.TrySetCanceled(_cancellationToken);
                _pipe.Writer.Complete(new OperationCanceledException(_cancellationToken));
            }

            public async Task<HttpResponseMessage> GetResponseAsync()
            {
                return await _responseTask.Task.ConfigureAwait(false);
            }

            public Stream ToStream()
            {
                return _pipe.Reader.AsStream();
            }
        }
        
#if USE_FUNCTION_POINTER
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_runtime_on_status_code_and_headers_receive_delegate))]
        private static unsafe void OnStatusCodeAndHeaderReceive(int reqSeq, int statusCode, YahaHttpVersion version)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{reqSeq}] Status code and headers received: StatusCode={statusCode}; Version={version}");

            if (_inflightRequests.TryGetValue(reqSeq, out var requestContext))
            {
                requestContext.Response.SetVersion(version);

                VerifyPointer(requestContext._ctx, requestContext._requestContext);
                var headersCount = NativeMethods.yaha_request_response_get_headers_count(requestContext._ctx, requestContext._requestContext);
                if (headersCount > 0)
                {
                    for (var i = 0; i < headersCount; i++)
                    {
                        var bufKey = NativeMethods.yaha_request_response_get_header_key(requestContext._ctx, requestContext._requestContext, i); 
                        var bufValue = NativeMethods.yaha_request_response_get_header_value(requestContext._ctx, requestContext._requestContext, i); 
                        try
                        {
                            requestContext.Response.SetHeader(bufKey->AsSpan(), bufValue->AsSpan());
                        }
                        finally
                        {
                            NativeMethods.yaha_free_byte_buffer(bufKey);
                            NativeMethods.yaha_free_byte_buffer(bufValue);
                        }
                    }
                }
                requestContext.Response.SetStatusCode(statusCode);
            }
        }

#if USE_FUNCTION_POINTER
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_runtime_on_receive_delegate))]
        private static unsafe void OnReceive(int reqSeq, UIntPtr length, byte* buf)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{reqSeq}] Response data received: Length={length}");

            var bufSpan = new Span<byte>(buf, (int)length);
            _inflightRequests[reqSeq].Response.Write(bufSpan);
        }

#if USE_FUNCTION_POINTER
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_runtime_on_complete_delegate))]
        private static unsafe void OnComplete(int reqSeq, byte hasError)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{reqSeq}] Response completed: HasError={hasError}");

            if (_inflightRequests.TryGetValue(reqSeq, out var requestContext))
            {
                VerifyPointer(requestContext._ctx, requestContext._requestContext);
                if (hasError == 0)
                {
                    var trailersCount = NativeMethods.yaha_request_response_get_trailers_count(requestContext._ctx, requestContext._requestContext);
                    if (trailersCount > 0)
                    {
                        for (var i = 0; i < trailersCount; i++)
                        {
                            var bufKey = NativeMethods.yaha_request_response_get_trailers_key(requestContext._ctx, requestContext._requestContext, i); 
                            var bufValue = NativeMethods.yaha_request_response_get_trailers_value(requestContext._ctx, requestContext._requestContext, i); 
                            try
                            {
                                requestContext.Response.SetTrailer(bufKey->AsSpan(), bufValue->AsSpan());
                            }
                            finally
                            {
                                NativeMethods.yaha_free_byte_buffer(bufKey);
                                NativeMethods.yaha_free_byte_buffer(bufValue);
                            }
                        }
                    }

                    requestContext.Response.Complete();
                }
                else
                {
                    var buf = NativeMethods.yaha_get_last_error();
                    try
                    {
                        requestContext.Response.CompleteAsFailed(UnsafeUtilities.GetStringFromUtf8Bytes(buf->AsSpan()));
                    }
                    catch
                    {
                        NativeMethods.yaha_free_byte_buffer(buf);
                    }
                }
            }
            _inflightRequests.TryRemove(reqSeq, out _);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
            }

            unsafe
            {
                if (_ctx != null)
                {
                    if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Disposing NativeLibraryWrapper");
                    NativeMethods.yaha_dispose_runtime(_ctx);
                    _ctx = null;
                }
            }
        }
    }
}
