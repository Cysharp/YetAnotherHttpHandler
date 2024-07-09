using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    internal class YetAnotherHttpHttpContent : HttpContent
    {
        private readonly RequestContext _requestContext;
        private readonly PipeReader _pipeReader;

        internal YetAnotherHttpHttpContent(RequestContext requestContext, PipeReader pipeReader)
        {
            _requestContext = requestContext;
            _pipeReader = pipeReader;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, default);

#if NET5_0_OR_GREATER
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
#else
        protected async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
#endif
        {
#if UNITY_2021_1_OR_NEWER
            // NOTE: Unity's Mono has older implementations of HttpClient and HttpContent.
            //       We need to wrap exceptions in HttpRequestException to align to the behavior of the .NET runtime.
            try
            {
                await _pipeReader.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                await _pipeReader.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                throw new HttpRequestException("Error while copying content to a stream.", e);
            }
#else
            await _pipeReader.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            await _pipeReader.CompleteAsync().ConfigureAwait(false);
#endif
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => CreateContentReadStreamAsync(default);

#if NET5_0_OR_GREATER
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
#else
        protected Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
#endif
        {
            return Task.FromResult<Stream>(_pipeReader.AsStream());
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestContext.RequestSequence}:State:0x{_requestContext.Handle:X}] Dispose YetAnotherHttpHttpContent: disposing={disposing}");

            if (disposing)
            {
                _pipeReader.Complete();
                _requestContext.TryAbort();
            }
            base.Dispose(disposing);
        }
    }
}
