using System;
using System.Diagnostics;
using System.Text;
using System.IO.Pipelines;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http.Shims;
#if UNITY_2019_1_OR_NEWER
using AOT;
#endif

namespace Cysharp.Net.Http
{
    internal class NativeHttpHandlerCore : IDisposable
    {
        private static int _requestSequence = 0;
        private static int _instanceId = 0;

        //private unsafe YahaNativeContext* _ctx;
        private readonly YahaContextSafeHandle _handle;
        private GCHandle? _onVerifyServerCertificateHandle; // The handle must be released in Dispose if it is allocated.
        private bool _disposed = false;
        private PipeOptions? _responsePipeOptions;

        // NOTE: We need to keep the callback delegates in advance.
        //       The delegates are kept on the Rust side, so it will crash if they are garbage collected.
        private static readonly unsafe NativeMethods.yaha_init_context_on_status_code_and_headers_receive_delegate OnStatusCodeAndHeaderReceiveCallback = OnStatusCodeAndHeaderReceive;
        private static readonly unsafe NativeMethods.yaha_init_context_on_receive_delegate OnReceiveCallback = OnReceive;
        private static readonly unsafe NativeMethods.yaha_init_context_on_complete_delegate OnCompleteCallback = OnComplete;
        private static readonly unsafe NativeMethods.yaha_client_config_set_server_certificate_verification_handler_handler_delegate OnServerCertificateVerificationCallback = OnServerCertificateVerification;

        public unsafe NativeHttpHandlerCore(NativeClientSettings settings)
        {
            var runtimeHandle = NativeRuntime.Instance.Acquire(); // NOTE: We need to call Release on finalizer.
            var instanceId = Interlocked.Increment(ref _instanceId);

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_init_context");
            var ctx = NativeMethods.yaha_init_context(runtimeHandle.DangerousGet(), OnStatusCodeAndHeaderReceiveCallback, OnReceiveCallback, OnCompleteCallback);
            _handle = new YahaContextSafeHandle(ctx, instanceId);
            _handle.SetParent(runtimeHandle);

            var addRefContextHandle = false;
            try
            {
                _handle.DangerousAddRef(ref addRefContextHandle);
                Initialize(_handle.DangerousGet(), settings);
            }
            catch
            {
                // NOTE: If the initialization fails, we need to release the runtime.
                NativeRuntime.Instance.Release();
                throw;
            }
            finally
            {
                if (addRefContextHandle)
                {
                    _handle.DangerousRelease();
                }
            }
        }

        private unsafe void Initialize(YahaNativeContext* ctx, NativeClientSettings settings)
        {
            if (settings.PoolIdleTimeout is { } poolIdleTimeout)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.PoolIdleTimeout)}' = {poolIdleTimeout}");
                NativeMethods.yaha_client_config_pool_idle_timeout(ctx, (ulong)poolIdleTimeout.TotalMilliseconds);
            }
            if (settings.MaxIdlePerHost is { } maxIdlePerHost)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.MaxIdlePerHost)}' = {maxIdlePerHost}");
                NativeMethods.yaha_client_config_pool_max_idle_per_host(ctx, (nuint)maxIdlePerHost);
            }
            if (settings.SkipCertificateVerification is { } skipCertificateVerification)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.SkipCertificateVerification)}' = {skipCertificateVerification}");
                NativeMethods.yaha_client_config_skip_certificate_verification(ctx, skipCertificateVerification);
            }
            if (settings.OnVerifyServerCertificate is { } onVerifyServerCertificate)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.OnVerifyServerCertificate)}' = {onVerifyServerCertificate}");

                // NOTE: We need to keep the handle to call in the static callback method.
                //       The handle must be released in Dispose if it is allocated.
                _onVerifyServerCertificateHandle = GCHandle.Alloc(onVerifyServerCertificate);

                NativeMethods.yaha_client_config_set_server_certificate_verification_handler(ctx, OnServerCertificateVerificationCallback, GCHandle.ToIntPtr(_onVerifyServerCertificateHandle.Value));
            }
            if (settings.RootCertificates is { } rootCertificates)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.RootCertificates)}' = Length:{rootCertificates.Length}");
                var rootCertificatesBytes = Encoding.UTF8.GetBytes(rootCertificates);
                fixed (byte* buffer = rootCertificatesBytes)
                {
                    var sb = new StringBuffer(buffer, rootCertificates.Length);
                    var validCertificatesCount = NativeMethods.yaha_client_config_add_root_certificates(ctx, &sb);
                    if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_client_config_add_root_certificates: ValidCertificatesCount={validCertificatesCount}");
                }
            }
            if (settings.OverrideServerName is { } overrideServerName)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.OverrideServerName)}' = {overrideServerName}");
                var overrideServerNameBytes = Encoding.UTF8.GetBytes(overrideServerName);
                fixed (byte* buffer = overrideServerNameBytes)
                {
                    var sb = new StringBuffer(buffer, overrideServerNameBytes.Length);
                    NativeMethods.yaha_client_config_add_override_server_name(ctx, &sb);
                }
            }
            if (settings.ClientAuthKey is { } clientAuthKey)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.ClientAuthKey)}' = {clientAuthKey}");
                var strBytes = Encoding.UTF8.GetBytes(clientAuthKey);
                fixed (byte* buffer = strBytes)
                {
                    var sb = new StringBuffer(buffer, strBytes.Length);
                    NativeMethods.yaha_client_config_add_client_auth_key(ctx, &sb);
                }
            }
            if (settings.ClientAuthCertificates is { } clientAuthCertificates)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.ClientAuthCertificates)}' = {clientAuthCertificates}");
                var strBytes = Encoding.UTF8.GetBytes(clientAuthCertificates);
                fixed (byte* buffer = strBytes)
                {
                    var sb = new StringBuffer(buffer, strBytes.Length);
                    NativeMethods.yaha_client_config_add_client_auth_certificates(ctx, &sb);
                }
            }
            if (settings.Http2Only is { } http2Only)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2Only)}' = {http2Only}");
                NativeMethods.yaha_client_config_http2_only(ctx, http2Only);
            }
            if (settings.Http2InitialStreamWindowSize is { } http2InitialStreamWindowSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2InitialStreamWindowSize)}' = {http2InitialStreamWindowSize}");
                NativeMethods.yaha_client_config_http2_initial_stream_window_size(ctx, http2InitialStreamWindowSize);
            }
            if (settings.Http2InitialConnectionWindowSize is { } http2InitialConnectionWindowSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2InitialConnectionWindowSize)}' = {http2InitialConnectionWindowSize}");
                NativeMethods.yaha_client_config_http2_initial_connection_window_size(ctx, http2InitialConnectionWindowSize);
            }
            if (settings.Http2AdaptiveWindow is { } http2AdaptiveWindow)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2AdaptiveWindow)}' = {http2AdaptiveWindow}");
                NativeMethods.yaha_client_config_http2_adaptive_window(ctx, http2AdaptiveWindow);
            }
            if (settings.Http2MaxFrameSize is { } http2MaxFrameSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxFrameSize)}' = {http2MaxFrameSize}");
                NativeMethods.yaha_client_config_http2_max_frame_size(ctx, http2MaxFrameSize);
            }
            if (settings.ConnectTimeout is { } connectTimeout)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.ConnectTimeout)}' = {connectTimeout}");
                NativeMethods.yaha_client_config_connect_timeout(ctx, (ulong)connectTimeout.TotalMilliseconds);
            }
            if (settings.Http2KeepAliveInterval is { } http2KeepAliveInterval)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveInterval)}' = {http2KeepAliveInterval}");
                NativeMethods.yaha_client_config_http2_keep_alive_interval(ctx, (ulong)http2KeepAliveInterval.TotalMilliseconds);
            }
            if (settings.Http2KeepAliveTimeout is { } http2KeepAliveTimeout)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveTimeout)}' = {http2KeepAliveTimeout}");
                NativeMethods.yaha_client_config_http2_keep_alive_timeout(ctx, (ulong)http2KeepAliveTimeout.TotalMilliseconds);
            }
            if (settings.Http2KeepAliveWhileIdle is { } http2KeepAliveWhileIdle)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveWhileIdle)}' = {http2KeepAliveWhileIdle}");
                NativeMethods.yaha_client_config_http2_keep_alive_while_idle(ctx, http2KeepAliveWhileIdle);
            }
            if (settings.Http2MaxConcurrentResetStreams is { } http2MaxConcurrentResetStreams)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxConcurrentResetStreams)}' = {http2MaxConcurrentResetStreams}");
                NativeMethods.yaha_client_config_http2_max_concurrent_reset_streams(ctx, (nuint)http2MaxConcurrentResetStreams);
            }
            if (settings.Http2MaxSendBufferSize is { } http2MaxSendBufferSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxSendBufferSize)}' = {http2MaxSendBufferSize}");
                NativeMethods.yaha_client_config_http2_max_send_buf_size(ctx, (nuint)http2MaxSendBufferSize);
            }
            if (settings.Http2InitialMaxSendStreams is { } http2InitialMaxSendStreams)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2InitialMaxSendStreams)}' = {http2InitialMaxSendStreams}");
                NativeMethods.yaha_client_config_http2_initial_max_send_streams(ctx, (nuint)http2InitialMaxSendStreams);
            }
            if (settings.UnixDomainSocketPath is { } unixDomainSocketPath)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.UnixDomainSocketPath)}' = {unixDomainSocketPath}");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    throw new PlatformNotSupportedException("Unix domain socket is not supported on Windows.");
                }
                var strBytes = Encoding.UTF8.GetBytes(unixDomainSocketPath);
                fixed (byte* buffer = strBytes)
                {
                    var sb = new StringBuffer(buffer, strBytes.Length);
                    NativeMethods.yaha_client_config_unix_domain_socket_path(ctx, &sb);
                }
            }

            if (settings.ResponsePipeOptions is { } responsePipeOptions)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.ResponsePipeOptions)}' = {responsePipeOptions}");
                _responsePipeOptions = responsePipeOptions;
            }

            NativeMethods.yaha_build_client(ctx);

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"{nameof(NativeHttpHandlerCore)} created");
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"HttpMessageHandler.SendAsync: {request.RequestUri}; Method={request.Method}; Version={request.Version}");

            var requestContext = Send(request, cancellationToken);

            if (request.Content != null)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Start sending the request body: {request.Content!.GetType().FullName}");
                _ = SendBodyAsync(request.Content!, requestContext.Writer, cancellationToken);
            }
            else
            {
                requestContext.Writer.Complete();
            }

            return requestContext.Response.GetResponseAsync();

            static async Task SendBodyAsync(HttpContent requestContent, PipeWriter writer, CancellationToken cancellationToken)
            {
                await requestContent.CopyToAsync(writer.AsStream()).ConfigureAwait(false); // TODO: cancel
                await writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        private unsafe RequestContext Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.RequestUri is null)
            {
                throw new InvalidOperationException("A request URI cannot be null.");
            }

            var requestSequence = Interlocked.Increment(ref _requestSequence);

            var addRefContext = false;
            try
            {
                _handle.DangerousAddRef(ref addRefContext);

                var ctx = _handle.DangerousGet();
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_request_new: requestSequence={requestSequence}");
                var reqCtx = NativeMethods.yaha_request_new(ctx, requestSequence);
                var reqCtxHandle = new YahaRequestContextSafeHandle(reqCtx, requestSequence);
                reqCtxHandle.SetParent(_handle);

                var addRefReqContext = false;
                try
                {
                    reqCtxHandle.DangerousAddRef(ref addRefReqContext);

                    return UnsafeSend(_handle, reqCtxHandle, requestSequence, request, cancellationToken);
                }
                finally
                {
                    if (addRefReqContext)
                    {
                        reqCtxHandle.DangerousRelease();
                    }
                }
            }
            finally
            {
                if (addRefContext)
                {
                    _handle.DangerousRelease();
                }
            }
        }

        private unsafe RequestContext UnsafeSend(YahaContextSafeHandle ctxHandle, YahaRequestContextSafeHandle reqCtxHandle, int requestSequence, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // NOTE: DangerousAddRef/Release has already called by caller (`Send`).
            var ctx = ctxHandle.DangerousGet();
            var reqCtx = reqCtxHandle.DangerousGet();

            // Set request headers
            SetRequestHeaders(request.Headers, ctx, reqCtx);
            if (request.Content is not null)
            {
                SetRequestHeaders(request.Content.Headers, ctx, reqCtx);
            }

            // Set HTTP method
            {
                using var strBytes = Utf8Strings.HttpMethods.FromHttpMethod(request.Method);
                fixed (byte* p = strBytes.Span)
                {
                    var buf = new StringBuffer(p, strBytes.Span.Length);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_method(ctx, reqCtx, &buf));
                }
            }

            // Set URI
            {
                using var strBytes = new TempUtf8String(UriHelper.ToSafeUriString(request.RequestUri));
                fixed (byte* p = strBytes.Span)
                {
                    var buf = new StringBuffer(p, strBytes.Span.Length);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_uri(ctx, reqCtx, &buf));
                }
            }

            // Set HTTP version
            // NOTE: Following reasons are why we should ignore HttpRequestMessage.Version.
            //       - If the request version is specified, Hyper expects it to match the version provided by the server, so specifying HttpVersion.Version20 will cause an error even when connecting to an HTTP/1 server over HTTPS.
            //       - The official .NET default behavior allows downgrading the HTTP request version.
            //       - If http2_only is not specified or false in Hyper, the version is determined by ALPN negotiation in TLS, so there is no need to set the version of the request.
            //       - If the server supports HTTP/2, HTTP/2 is selected by ALPN or h2c is used with http2_only, so there is no need to specify the request version.
            //var version = request.Version switch
            //{
            //    var v when v == HttpVersion.Version10 => YahaHttpVersion.Http10,
            //    var v when v == HttpVersion.Version11 => YahaHttpVersion.Http11,
            //    var v when v == HttpVersionShim.Version20 => YahaHttpVersion.Http2,
            //    _ => throw new NotSupportedException($"Unsupported HTTP version '{request.Version}'"),
            //};
            //NativeMethods.yaha_request_set_version(ctx, reqCtx, version);

            // Prepare body channel
            NativeMethods.yaha_request_set_has_body(ctx, reqCtx, request.Content != null);

            // Prepare a request context
            var requestContextManaged = new RequestContext(_handle, reqCtxHandle, request, requestSequence, _responsePipeOptions, cancellationToken);
            requestContextManaged.Allocate();
            if (cancellationToken.IsCancellationRequested)
            {
                // Dispose the request context immediately.
                requestContextManaged.Release();
                requestContextManaged.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Begin request
            try
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{requestSequence}:State:0x{requestContextManaged.Handle:X}] Begin HTTP request to the server.");
                ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_begin(ctx, reqCtx, requestContextManaged.Handle));
                requestContextManaged.Start(hasBody: request.Content != null); // NOTE: ReadRequestLoop must be started after `request_begin`.
            }
            catch
            {
                requestContextManaged.Release();
                requestContextManaged.Dispose();
                throw;
            }

            return requestContextManaged;
        }

        private static unsafe void SetRequestHeaders(HttpHeaders headers, YahaNativeContext* ctx, YahaNativeRequestContext* reqCtx)
        {
            foreach (var header in headers)
            {
                using var key = new TempUtf8String(header.Key);
                using var value = new TempUtf8String(string.Join(",", header.Value));
                fixed (byte* pKey = key.Span)
                fixed (byte* pValue = value.Span)
                {
                    var bufKey = new StringBuffer(pKey, key.Span.Length);
                    var bufValue = new StringBuffer(pValue, value.Span.Length);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_header(ctx, reqCtx, &bufKey, &bufValue));
                }
            }
        }

        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_context_on_status_code_and_headers_receive_delegate))]
        private static unsafe void OnStatusCodeAndHeaderReceive(int reqSeq, IntPtr state, int statusCode, YahaHttpVersion version)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{reqSeq}:State:0x{state:X}] Status code and headers received: StatusCode={statusCode}; Version={version}");

            var requestContext = RequestContext.FromHandle(state);
            requestContext.Response.SetVersion(version);

            var addRefContext = false;
            var addRefRequestContext = false;
            try
            {
                requestContext._ctxHandle.DangerousAddRef(ref addRefContext);
                requestContext._requestContextHandle.DangerousAddRef(ref addRefRequestContext);

                var ctx = requestContext._ctxHandle.DangerousGet();
                var reqCtx = requestContext._requestContextHandle.DangerousGet();

                var headersCount = NativeMethods.yaha_request_response_get_headers_count(ctx, reqCtx);
                if (headersCount > 0)
                {
                    for (var i = 0; i < headersCount; i++)
                    {
                        var bufKey = NativeMethods.yaha_request_response_get_header_key(ctx, reqCtx, i);
                        var bufValue = NativeMethods.yaha_request_response_get_header_value(ctx, reqCtx, i);
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
            }
            finally
            {
                if (addRefContext)
                {
                    requestContext._ctxHandle.DangerousRelease();
                }
                if (addRefRequestContext)
                {
                    requestContext._requestContextHandle.DangerousRelease();
                }
            }

            requestContext.Response.SetStatusCode(statusCode);
        }

        [MonoPInvokeCallback(typeof(NativeMethods.yaha_client_config_set_server_certificate_verification_handler_handler_delegate))]
        private static unsafe bool OnServerCertificateVerification(IntPtr callbackState, byte* serverNamePtr, UIntPtr /*nuint*/ serverNameLength, byte* certificateDerPtr, UIntPtr /*nuint*/ certificateDerLength, ulong now)
        {
            var serverName = UnsafeUtilities.GetStringFromUtf8Bytes(new ReadOnlySpan<byte>(serverNamePtr, (int)serverNameLength));
            var certificateDer = new ReadOnlySpan<byte>(certificateDerPtr, (int)certificateDerLength);
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"OnServerCertificateVerification: State=0x{callbackState:X}; ServerName={serverName}; CertificateDer.Length={certificateDer.Length}; Now={now}");

            var onServerCertificateVerification = (ServerCertificateVerificationHandler?)GCHandle.FromIntPtr(callbackState).Target;
            Debug.Assert(onServerCertificateVerification != null);
            if (onServerCertificateVerification == null)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Warning($"OnServerVerification: The verification callback was called, but onServerCertificateVerification is null.");
                return false;
            }
            try
            {
                var success = onServerCertificateVerification(serverName, certificateDer, DateTimeOffset.FromUnixTimeSeconds((long)now));
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"OnServerVerification: Success = {success}");
                return success;
            }
            catch (Exception e)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Error($"OnServerVerification: The verification callback thrown an exception: {e.ToString()}");
                return false;
            }
        }

        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_context_on_receive_delegate))]
        private static unsafe void OnReceive(int reqSeq, IntPtr state, UIntPtr length, byte* buf, nuint taskHandle)
        {
            try
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{reqSeq}:State:0x{state:X}] Response data received: Length={length}");

                var bufSpan = new Span<byte>(buf, (int)length);
                var requestContext = RequestContext.FromHandle(state);
                var write = requestContext.Response.WriteAsync(bufSpan);

                if (write.IsCompleted)
                {
                    write.GetAwaiter().GetResult();
                    CompleteTask(taskHandle);
                }
                else
                {
                    // backpressure is occurred
                    var awaiter = write.GetAwaiter();
                    awaiter.UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            awaiter.GetResult();
                        }
                        catch (Exception ex)
                        {
                            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Error($"[ReqSeq:{reqSeq}:State:0x{state:X}] Failed to flush response data: {ex}");
                            CompleteTask(taskHandle, ex.ToString());
                            return;
                        }

                        CompleteTask(taskHandle);
                    });
                }
            }
            catch (Exception ex)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Error($"[ReqSeq:{reqSeq}:State:0x{state:X}] Failed to flush response data: {ex}");
                CompleteTask(taskHandle, ex.ToString());
            }
        }

        private static unsafe void CompleteTask(nuint taskHandle, string? error = null)
        {
            if (error is null)
            {
                NativeMethods.yaha_complete_task(taskHandle, (StringBuffer*)0);
                return;
            }

            using var messageUtf8 = new TempUtf8String(error);
            fixed (byte* messagePtr = messageUtf8.Span)
            {
                var sb = new StringBuffer(messagePtr, messageUtf8.Span.Length);
                NativeMethods.yaha_complete_task((nuint)(nint)taskHandle, &sb);
            }
        }

        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_context_on_complete_delegate))]
        private static unsafe void OnComplete(int reqSeq, IntPtr state, CompletionReason reason, uint h2ErrorCode)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{reqSeq}:State:0x{state:X}] Response completed: Reason={reason}; H2ErrorCode=0x{h2ErrorCode:x}");

            var requestContext = RequestContext.FromHandle(state);
            try
            {
                if (reason == CompletionReason.Success)
                {
                    var addRefContext = false;
                    var addRefRequestContext = false;
                    try
                    {
                        requestContext._ctxHandle.DangerousAddRef(ref addRefContext);
                        requestContext._requestContextHandle.DangerousAddRef(ref addRefRequestContext);

                        var ctx = requestContext._ctxHandle.DangerousGet();
                        var reqCtx = requestContext._requestContextHandle.DangerousGet();

                        var trailersCount = NativeMethods.yaha_request_response_get_trailers_count(ctx, reqCtx);
                        if (trailersCount > 0)
                        {
                            for (var i = 0; i < trailersCount; i++)
                            {
                                var bufKey = NativeMethods.yaha_request_response_get_trailers_key(ctx, reqCtx, i);
                                var bufValue = NativeMethods.yaha_request_response_get_trailers_value(ctx, reqCtx, i);
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
                    }
                    finally
                    {
                        if (addRefContext)
                        {
                            requestContext._ctxHandle.DangerousRelease();
                        }
                        if (addRefRequestContext)
                        {
                            requestContext._requestContextHandle.DangerousRelease();
                        }
                    }

                    requestContext.Complete();
                }
                else if (reason == CompletionReason.Error)
                {
                    var buf = NativeMethods.yaha_get_last_error();
                    try
                    {
                        requestContext.CompleteAsFailed(UnsafeUtilities.GetStringFromUtf8Bytes(buf->AsSpan()), h2ErrorCode);
                    }
                    catch
                    {
                        NativeMethods.yaha_free_byte_buffer(buf);
                    }
                }
                else
                {
                    requestContext.CompleteAsFailed("Canceled", 0);
                }
            }
            finally
            {
                requestContext.Release();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Dispose {nameof(NativeHttpHandlerCore)}; disposing={disposing}");

            _onVerifyServerCertificateHandle?.Free();

            NativeRuntime.Instance.Release(); // We always need to release runtime.

            if (disposing)
            {
                _handle.Dispose();
            }

            _disposed = true;
        }
    }
}
