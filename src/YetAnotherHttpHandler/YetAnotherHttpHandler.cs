using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    /// <summary>
    /// Provides a message handler for HttpClient based on the native HTTP/2 backend.
    /// </summary>
    public class YetAnotherHttpHandler : HttpMessageHandler
    {
        private readonly NativeClientSettings _settings = new NativeClientSettings();
        private bool _disposed;
        private NativeHttpHandlerCore? _handler;

        /// <summary>
        /// Gets or sets an optional timeout for idle sockets being kept-alive. Default is 90 seconds.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.pool_idle_timeout">hyper: pool_idle_timeout</see>
        /// </remarks>
        public TimeSpan? PoolIdleTimeout { get => _settings.PoolIdleTimeout; set => _settings.PoolIdleTimeout = value; }

        /// <summary>
        /// Gets or sets the maximum idle connection per host allowed in the pool. Default is usize::MAX (no limit).
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.pool_max_idle_per_host">hyper: pool_max_idle_per_host</see>
        /// </remarks>
        public ulong? MaxIdlePerHost { get => _settings.MaxIdlePerHost; set => _settings.MaxIdlePerHost = value; }

        /// <summary>
        /// Gets or sets a value that indicates whether to force the use of HTTP/2.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_only">hyper: http2_only</see>
        /// </remarks>
        public bool? Http2Only { get => _settings.Http2Only; set => _settings.Http2Only = value; }

        /// <summary>
        /// Gets or sets a value that indicates whether to skip certificate verification.
        /// </summary>
        public bool? SkipCertificateVerification { get => _settings.SkipCertificateVerification; set => _settings.SkipCertificateVerification = value; }

        /// <summary>
        /// Gets or sets a custom handler that validates server certificates.
        /// </summary>
        public ServerCertificateVerificationHandler? OnVerifyServerCertificate { get => _settings.OnVerifyServerCertificate; set => _settings.OnVerifyServerCertificate = value; }

        /// <summary>
        /// Gets or sets a custom root CA. By default, the built-in root CA (Mozilla's root certificates) is used. See also <seealso href="https://github.com/rustls/webpki-roots" />.
        /// </summary>
        public string? RootCertificates { get => _settings.RootCertificates; set => _settings.RootCertificates = value; }

        /// <summary>
        /// Gets or sets a value that specifies subject alternative name (SAN) of the certificate.
        /// </summary>
        public string? OverrideServerName { get => _settings.OverrideServerName; set => _settings.OverrideServerName = value; }

        /// <summary>
        /// Gets or sets a custom client auth certificates.
        /// </summary>
        public string? ClientAuthCertificates { get => _settings.ClientAuthCertificates; set => _settings.ClientAuthCertificates = value; }

        /// <summary>
        /// Gets or sets a custom client auth key.
        /// </summary>
        public string? ClientAuthKey { get => _settings.ClientAuthKey; set => _settings.ClientAuthKey = value; }

        /// <summary>
        /// Gets or sets the SETTINGS_INITIAL_WINDOW_SIZE option for HTTP2 stream-level flow control.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_initial_stream_window_size">hyper: http2_initial_stream_window_size</see>
        /// </remarks>
        public uint? Http2InitialStreamWindowSize { get => _settings.Http2InitialStreamWindowSize; set => _settings.Http2InitialStreamWindowSize = value; }

        /// <summary>
        /// Gets or sets the max connection-level flow control for HTTP2
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_initial_connection_window_size">hyper: http2_initial_connection_window_size</see>
        /// </remarks>
        public uint? Http2InitialConnectionWindowSize { get => _settings.Http2InitialConnectionWindowSize; set => _settings.Http2InitialConnectionWindowSize = value; }

        /// <summary>
        /// Gets or sets whether to use an adaptive flow control. Enabling this will override the limits set in http2_initial_stream_window_size and http2_initial_connection_window_size.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_adaptive_window">hyper: http2_adaptive_window</see>
        /// </remarks>
        public bool? Http2AdaptiveWindow { get => _settings.Http2AdaptiveWindow; set => _settings.Http2AdaptiveWindow = value; }

        /// <summary>
        /// Gets or sets the maximum frame size to use for HTTP2.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_max_frame_size">hyper: http2_max_frame_size</see>
        /// </remarks>
        public uint? Http2MaxFrameSize { get => _settings.Http2MaxFrameSize; set => _settings.Http2MaxFrameSize = value; }
        
        /// <summary>
        /// Gets or sets timeout for TCP connection establishment
        /// Pass <value>null</value> to never timeout.
        /// Default is never timeout.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/connect/struct.HttpConnector.html#method.set_connect_timeout">hyper: set_connect_timeout</see>
        /// </remarks>
        public TimeSpan? ConnectTimeout { get => _settings.ConnectTimeout; set => _settings.ConnectTimeout = value; }

        /// <summary>
        /// Gets or sets an interval for HTTP2 Ping frames should be sent to keep a connection alive.
        /// Pass <value>null</value> to disable HTTP2 keep-alive.
        /// Default is currently disabled.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_keep_alive_interval">hyper: http2_keep_alive_interval</see>
        /// </remarks>
        public TimeSpan? Http2KeepAliveInterval { get => _settings.Http2KeepAliveInterval; set => _settings.Http2KeepAliveInterval = value; }

        /// <summary>
        /// Gets or sets a timeout for receiving an acknowledgement of the keep-alive ping.
        /// If the ping is not acknowledged within the timeout, the connection will be closed. Does nothing if http2_keep_alive_interval is disabled.
        /// Default is 20 seconds.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_keep_alive_timeout">hyper: http2_keep_alive_timeout</see>
        /// </remarks>
        public TimeSpan? Http2KeepAliveTimeout { get => _settings.Http2KeepAliveTimeout; set => _settings.Http2KeepAliveTimeout = value; }

        /// <summary>
        /// Gets or sets whether HTTP2 keep-alive should apply while the connection is idle.
        /// If disabled, keep-alive pings are only sent while there are open request/responses streams. If enabled, pings are also sent when no streams are active. Does nothing if http2_keep_alive_interval is disabled.
        /// Default is false.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_keep_alive_while_idle">hyper: http2_keep_alive_while_idle</see>
        /// </remarks>
        public bool? Http2KeepAliveWhileIdle { get => _settings.Http2KeepAliveWhileIdle; set => _settings.Http2KeepAliveWhileIdle = value; }

        /// <summary>
        /// Gets or sets the maximum number of HTTP2 concurrent locally reset streams.
        /// See the documentation of h2::client::Builder::max_concurrent_reset_streams for more details.
        /// The default value is determined by the h2 crate.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_max_concurrent_reset_streams">hyper: http2_max_concurrent_reset_streams</see>
        /// </remarks>
        public ulong? Http2MaxConcurrentResetStreams { get => _settings.Http2MaxConcurrentResetStreams; set => _settings.Http2MaxConcurrentResetStreams = value; }

        /// <summary>
        /// Gets or sets the maximum write buffer size for each HTTP/2 stream.
        /// Default is currently 1MB, but may change.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_max_send_buf_size">hyper: http2_max_send_buf_size</see>
        /// </remarks>
        public ulong? Http2MaxSendBufferSize { get => _settings.Http2MaxSendBufferSize; set => _settings.Http2MaxSendBufferSize = value; }

        /// <summary>
        /// Gets or sets the initial maximum of locally initiated (send) streams.
        /// This value will be overwritten by the value included in the initial SETTINGS frame received from the peer as part of a connection preface.
        /// </summary>
        /// <remarks>
        /// <see href="https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html#method.http2_initial_max_send_streams">hyper_util: http2_initial_max_send_streams</see>
        /// </remarks>
        public ulong? Http2InitialMaxSendStreams { get => _settings.Http2InitialMaxSendStreams; set => _settings.Http2InitialMaxSendStreams = value; }

        /// <summary>
        /// Gets or sets the path to a Unix Domain Socket to be used as HTTP communication channel instead of the default TCP.
        /// </summary>
        public string? UnixDomainSocketPath { get => _settings.UnixDomainSocketPath; set => _settings.UnixDomainSocketPath = value; }

        /// <summary>
        /// Gets or sets the options for the pipe used to receive the response body.
        /// </summary>
        public PipeOptions? ResponsePipeOptions { get => _settings.ResponsePipeOptions; set => _settings.ResponsePipeOptions = value; }

        /// <summary>
        /// Sets the number of worker threads to use for the tokio runtime. If null, the default number of threads (number of cores) will be used.
        /// </summary>
        /// <remarks>
        /// The number of worker threads is set when the tokio runtime is initialized. This method should be called before any HTTP requests are made.
        /// </remarks>
        /// <param name="workerThreads"></param>
        public static void SetWorkerThreads(int? workerThreads)
        {
            NativeRuntime.Instance.SetWorkerThreads(workerThreads);
        }

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var handler = _handler ?? SetupHandler();
            return handler.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            _handler?.Dispose();
            _handler = null;
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }

    /// <summary>
    /// Represents a method that validates server certificates.
    /// </summary>
    /// <param name="serverName">The server host name.</param>
    /// <param name="certificate">The server certificate in DER format.</param>
    /// <param name="now">The current time.</param>
    /// <returns></returns>
    public delegate bool ServerCertificateVerificationHandler(string serverName, ReadOnlySpan<byte> certificate, DateTimeOffset now);

    internal class NativeClientSettings
    {
        public TimeSpan? PoolIdleTimeout { get; set; }
        public ulong? MaxIdlePerHost { get; set; }
        public bool? Http2Only { get; set; }
        public bool? SkipCertificateVerification { get; set; }
        public ServerCertificateVerificationHandler? OnVerifyServerCertificate { get; set; }
        public string? RootCertificates { get; set; }
        public string? OverrideServerName { get; set; }
        public string? ClientAuthCertificates { get; set; }
        public string? ClientAuthKey { get; set; }
        public uint? Http2InitialStreamWindowSize { get; set; }
        public uint? Http2InitialConnectionWindowSize { get; set; }
        public bool? Http2AdaptiveWindow { get; set; }
        public uint? Http2MaxFrameSize { get; set; }
        public TimeSpan? ConnectTimeout { get; set; }
        public TimeSpan? Http2KeepAliveInterval { get; set; }
        public TimeSpan? Http2KeepAliveTimeout { get; set; }
        public bool? Http2KeepAliveWhileIdle { get; set; }
        public ulong? Http2MaxConcurrentResetStreams { get; set; }
        public ulong? Http2MaxSendBufferSize { get; set; }
        public ulong? Http2InitialMaxSendStreams { get; set; }
        public string? UnixDomainSocketPath { get; set; }
        public PipeOptions? ResponsePipeOptions { get; set; }

        public NativeClientSettings Clone()
        {
            return new NativeClientSettings
            {
                PoolIdleTimeout = this.PoolIdleTimeout,
                MaxIdlePerHost = this.MaxIdlePerHost,
                Http2Only = this.Http2Only,
                SkipCertificateVerification = this.SkipCertificateVerification,
                OnVerifyServerCertificate = this.OnVerifyServerCertificate,
                RootCertificates = this.RootCertificates,
                OverrideServerName = this.OverrideServerName,
                ClientAuthCertificates = this.ClientAuthCertificates,
                ClientAuthKey = this.ClientAuthKey,
                Http2InitialStreamWindowSize = this.Http2InitialStreamWindowSize,
                Http2InitialConnectionWindowSize = this.Http2InitialConnectionWindowSize,
                Http2AdaptiveWindow = this.Http2AdaptiveWindow,
                Http2MaxFrameSize = this.Http2MaxFrameSize,
                ConnectTimeout = this.ConnectTimeout,
                Http2KeepAliveInterval = this.Http2KeepAliveInterval,
                Http2KeepAliveTimeout = this.Http2KeepAliveTimeout,
                Http2KeepAliveWhileIdle = this.Http2KeepAliveWhileIdle,
                Http2MaxConcurrentResetStreams = this.Http2MaxConcurrentResetStreams,
                Http2MaxSendBufferSize = this.Http2MaxSendBufferSize,
                Http2InitialMaxSendStreams = this.Http2InitialMaxSendStreams,
                UnixDomainSocketPath = this.UnixDomainSocketPath,
                ResponsePipeOptions = this.ResponsePipeOptions,
            };
        }
    }
}
