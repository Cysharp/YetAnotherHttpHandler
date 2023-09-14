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
        private readonly int _requestSequence;
        private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);
        private readonly TaskCompletionSource<HttpResponseMessage> _responseTask;
        private readonly HttpResponseMessage _message;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _tokenRegistration;

        public PipeReader Reader => _pipe.Reader;

        internal ResponseContext(HttpRequestMessage requestMessage, RequestContext requestContext, CancellationToken cancellationToken)
        {
            _responseTask = new TaskCompletionSource<HttpResponseMessage>();
            _requestSequence = requestContext.RequestSequence;
            _cancellationToken = cancellationToken;
            _tokenRegistration = cancellationToken.Register((state) =>
            {
                ((ResponseContext)state!).Cancel();
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
}