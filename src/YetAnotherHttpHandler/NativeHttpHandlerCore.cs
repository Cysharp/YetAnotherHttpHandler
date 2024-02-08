#if NET5_0_OR_GREATER
#define USE_FUNCTION_POINTER
#endif

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
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
        private bool _disposed = false;

        public unsafe NativeHttpHandlerCore(NativeClientSettings settings)
        {
            var runtimeHandle = NativeRuntime.Instance.Acquire(); // NOTE: We need to call Release on finalizer.
            var instanceId = Interlocked.Increment(ref _instanceId);

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_init_context");
#if USE_FUNCTION_POINTER
            var ctx = NativeMethodsFuncPtr.yaha_init_context(runtimeHandle.DangerousGet(), &OnStatusCodeAndHeaderReceive, &OnReceive, &OnComplete);
#else
            var ctx = NativeMethods.yaha_init_context(runtimeHandle.DangerousGet(), OnStatusCodeAndHeaderReceive, OnReceive, OnComplete);
#endif
            _handle = new YahaContextSafeHandle(ctx, instanceId);
            _handle.SetParent(runtimeHandle);

            var addRefContextHandle = false;
            try
            {
                _handle.DangerousAddRef(ref addRefContextHandle);
                Initialize(_handle.DangerousGet(), settings);
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

            NativeMethods.yaha_build_client(ctx);

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"{nameof(NativeHttpHandlerCore)} created");
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"HttpMessageHandler.SendAsync: {request.RequestUri}");

            var requestContext = Send(request, cancellationToken);
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
            var headers = request.Content is null
                ? request.Headers
                : request.Headers.Concat(request.Content.Headers);
            foreach (var header in headers)
            {
                var keyBytes = Encoding.UTF8.GetBytes(header.Key);
                var valueBytes = Encoding.UTF8.GetBytes(string.Join(",", header.Value));

                fixed (byte* pKey = keyBytes)
                fixed (byte* pValue = valueBytes)
                {
                    var bufKey = new StringBuffer(pKey, keyBytes.Length);
                    var bufValue = new StringBuffer(pValue, valueBytes.Length);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_header(ctx, reqCtx, &bufKey, &bufValue));
                }
            }

            // Set HTTP method
            {
                var strBytes = Encoding.UTF8.GetBytes(request.Method.ToString());
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_method(ctx, reqCtx, &buf));
                }
            }

            // Set URI
            {
                var strBytes = Encoding.UTF8.GetBytes( UriHelper.ToSafeUriString(request.RequestUri));
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_uri(ctx, reqCtx, &buf));
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
            NativeMethods.yaha_request_set_version(ctx, reqCtx, version);

            // Prepare body channel
            NativeMethods.yaha_request_set_has_body(ctx, reqCtx, request.Content != null);

            // Prepare a request context
            var requestContextManaged = new RequestContext(_handle, reqCtxHandle, request, requestSequence, cancellationToken);
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
                requestContextManaged.Start(); // NOTE: ReadRequestLoop must be started after `request_begin`.
            }
            catch
            {
                requestContextManaged.Release();
                requestContextManaged.Dispose();
                throw;
            }

            return requestContextManaged;
        }


#if USE_FUNCTION_POINTER
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
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

#if USE_FUNCTION_POINTER
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_context_on_receive_delegate))]
        private static unsafe void OnReceive(int reqSeq, IntPtr state, UIntPtr length, byte* buf)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{reqSeq}:State:0x{state:X}] Response data received: Length={length}");

            var bufSpan = new Span<byte>(buf, (int)length);
            var requestContext = RequestContext.FromHandle(state);
            requestContext.Response.Write(bufSpan);
        }

#if USE_FUNCTION_POINTER
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_context_on_complete_delegate))]
        private static unsafe void OnComplete(int reqSeq, IntPtr state, CompletionReason reason)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{reqSeq}:State:0x{state:X}] Response completed: Reason={reason}");

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

                    requestContext.Response.Complete();
                }
                else if (reason == CompletionReason.Error)
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
                else
                {
                    requestContext.Response.CompleteAsFailed("Canceled");
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

            NativeRuntime.Instance.Release(); // We always need to release runtime.

            if (disposing)
            {
                _handle.Dispose();
            }

            _disposed = true;
        }
    }
}
