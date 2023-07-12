using System;
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
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null)
            {
                throw new InvalidOperationException("RequestUri cannot be null.");
            }

            //Console.WriteLine($"{nameof(YetAnotherHttpHandler)}.SendAsync: Begin");

            var requestContext = _wrapper.Send(request);

            if (request.Content != null)
            {
                _ = Task.Run(async () =>
                {
                    //Console.WriteLine("request.Content.CopyToAsync: Begin");
                    await request.Content.CopyToAsync(requestContext.Writer.AsStream()).ConfigureAwait(false);
                    await requestContext.Writer.CompleteAsync().ConfigureAwait(false);
                    //Console.WriteLine("request.Content.CopyToAsync: End");
                });
            }
            else
            {
                await requestContext.Writer.CompleteAsync().ConfigureAwait(false);
            }

            //Console.WriteLine($"{nameof(YetAnotherHttpHandler)}.SendAsync: End");

            return await requestContext.Response.GetResponseAsync().ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            _wrapper.Dispose();
        }
    }
}
