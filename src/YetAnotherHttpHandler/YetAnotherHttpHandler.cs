using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    public class YetAnotherHttpHandler : HttpMessageHandler
    {
        private readonly NativeLibraryWrapper _wrapper;

        public YetAnotherHttpHandler()
        {
            _wrapper = new NativeLibraryWrapper();
            if (YahaEventSource.Log.IsEnabled) YahaEventSource.Log.Info($"YetAnotherHttpHandler created");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null)
            {
                throw new InvalidOperationException("RequestUri cannot be null.");
            }
            if (YahaEventSource.Log.IsEnabled) YahaEventSource.Log.Info($"HttpMessageHandler.SendAsync: {request.RequestUri}");

            var requestContext = _wrapper.Send(request, cancellationToken);
            if (request.Content != null)
            {
                if (YahaEventSource.Log.IsEnabled) YahaEventSource.Log.Info($"Start sending the request body: {request.Content.GetType().FullName}");
                _ = Task.Run(async () =>
                {
                    var src = await request.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await src.CopyToAsync(requestContext.Writer, cancellationToken).ConfigureAwait(false);
                    await requestContext.Writer.CompleteAsync().ConfigureAwait(false);
                });
            }
            else
            {
                await requestContext.Writer.CompleteAsync().ConfigureAwait(false);
            }

            return await requestContext.Response.GetResponseAsync().ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (YahaEventSource.Log.IsEnabled) YahaEventSource.Log.Info($"Disposing YetAnotherHttpHandler");
            _wrapper.Dispose();
        }
    }
}
