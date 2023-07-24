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
        private readonly NativeClientSettings _settings = new NativeClientSettings();
        private NativeHttpHandlerCore? _handler;

        public bool? Http2Only { get => _settings.Http2Only; set => _settings.Http2Only = value; }
        public bool? SkipCertificateVerification { get => _settings.SkipCertificateVerification; set => _settings.SkipCertificateVerification = value; }
        public uint? Http2InitialStreamWindowSize { get => _settings.Http2InitialStreamWindowSize; set => _settings.Http2InitialStreamWindowSize = value; }
        public uint? Http2InitialConnectionWindowSize { get => _settings.Http2InitialConnectionWindowSize; set => _settings.Http2InitialConnectionWindowSize = value; }
        public bool? Http2AdaptiveWindow { get => _settings.Http2AdaptiveWindow; set => _settings.Http2AdaptiveWindow = value; }
        public uint? Http2MaxFrameSize { get => _settings.Http2MaxFrameSize; set => _settings.Http2MaxFrameSize = value; }
        public TimeSpan? Http2KeepAliveInterval { get => _settings.Http2KeepAliveInterval; set => _settings.Http2KeepAliveInterval = value; }
        public TimeSpan? Http2KeepAliveTimeout { get => _settings.Http2KeepAliveTimeout; set => _settings.Http2KeepAliveTimeout = value; }
        public bool? Http2KeepAliveWhileIdle { get => _settings.Http2KeepAliveWhileIdle; set => _settings.Http2KeepAliveWhileIdle = value; }
        public ulong? Http2MaxConcurrentResetStreams { get => _settings.Http2MaxConcurrentResetStreams; set => _settings.Http2MaxConcurrentResetStreams = value; }
        public ulong? Http2MaxSendBufferSize { get => _settings.Http2MaxSendBufferSize; set => _settings.Http2MaxSendBufferSize = value; }

        private NativeHttpHandlerCore SetupHandler()
        {
            var settings = _settings.Clone();
            var handler = new NativeHttpHandlerCore(settings);

            if (Interlocked.CompareExchange(ref _handler, handler, null) != null)
            {
                handler.Dispose();
            }

            return _handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var handler = _handler ?? SetupHandler();
            return await handler.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            _handler?.Dispose();
        }
    }

    internal class NativeClientSettings
    {
        public bool? Http2Only { get; set; }
        public bool? SkipCertificateVerification { get; set; }
        public uint? Http2InitialStreamWindowSize { get; set; }
        public uint? Http2InitialConnectionWindowSize { get; set; }
        public bool? Http2AdaptiveWindow { get; set; }
        public uint? Http2MaxFrameSize { get; set; }
        public TimeSpan? Http2KeepAliveInterval { get; set; }
        public TimeSpan? Http2KeepAliveTimeout { get; set; }
        public bool? Http2KeepAliveWhileIdle { get; set; }
        public ulong? Http2MaxConcurrentResetStreams { get; set; }
        public ulong? Http2MaxSendBufferSize { get; set; }

        public NativeClientSettings Clone()
        {
            return new NativeClientSettings
            {
                Http2Only = this.Http2Only,
                SkipCertificateVerification = this.SkipCertificateVerification,
                Http2InitialStreamWindowSize = this.Http2InitialStreamWindowSize,
                Http2InitialConnectionWindowSize = this.Http2InitialConnectionWindowSize,
                Http2AdaptiveWindow = this.Http2AdaptiveWindow,
                Http2MaxFrameSize = this.Http2MaxFrameSize,
                Http2KeepAliveInterval = this.Http2KeepAliveInterval,
                Http2KeepAliveTimeout = this.Http2KeepAliveTimeout,
                Http2KeepAliveWhileIdle = this.Http2KeepAliveWhileIdle,
                Http2MaxConcurrentResetStreams = this.Http2MaxConcurrentResetStreams,
                Http2MaxSendBufferSize = this.Http2MaxSendBufferSize,
            };
        }
    }

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
                var src = await requestContent.ReadAsStreamAsync().ConfigureAwait(false);
                await src.CopyToAsync(writer, cancellationToken).ConfigureAwait(false);
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
