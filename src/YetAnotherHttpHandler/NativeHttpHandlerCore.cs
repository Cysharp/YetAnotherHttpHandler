using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    internal class NativeHttpHandlerCore : IDisposable
    {
        private readonly NativeLibraryWrapper _wrapper;

        public NativeHttpHandlerCore(NativeClientSettings settings)
        {
            _wrapper = new NativeLibraryWrapper(settings);
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"NativeHttpHandlerCore created");
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"HttpMessageHandler.SendAsync: {request.RequestUri}");

            var requestContext = _wrapper.Send(request, cancellationToken);
            if (request.Content != null)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Start sending the request body: {request.Content.GetType().FullName}");
                _ = SendBodyAsync(request.Content, requestContext.Writer, cancellationToken);
            }
            else
            {
                await requestContext.Writer.CompleteAsync().ConfigureAwait(false);
            }

            return await requestContext.Response.GetResponseAsync().ConfigureAwait(false);

            static async Task SendBodyAsync(HttpContent requestContent, PipeWriter writer, CancellationToken cancellationToken)
            {
                await requestContent.CopyToAsync(writer.AsStream()).ConfigureAwait(false); // TODO: cancel
                await writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Disposing NativeHttpHandlerCore");
            _wrapper.Dispose();
        }
    }
}