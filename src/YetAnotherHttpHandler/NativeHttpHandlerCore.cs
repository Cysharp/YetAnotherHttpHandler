#if NET5_0_OR_GREATER
#define USE_FUNCTION_POINTER
#endif

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Diagnostics;
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
        private unsafe YahaNativeContext* _ctx;
        private static ConcurrentDictionary<int, RequestContext> _inflightRequests = new ConcurrentDictionary<int, RequestContext>();
        private static int _requestSequence = 0;

        public unsafe NativeHttpHandlerCore(NativeClientSettings settings)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_init_runtime");
#if USE_FUNCTION_POINTER
            _ctx = NativeMethodsFuncPtr.yaha_init_runtime(&OnStatusCodeAndHeaderReceive, &OnReceive, &OnComplete);
#else
            _ctx = NativeMethods.yaha_init_runtime(OnStatusCodeAndHeaderReceive, OnReceive, OnComplete);
#endif

            if (settings.PoolIdleTimeout is { } poolIdleTimeout)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.PoolIdleTimeout)}' = {poolIdleTimeout}");
                NativeMethods.yaha_client_config_pool_idle_timeout(_ctx, (ulong)poolIdleTimeout.TotalMilliseconds);
            }
            if (settings.MaxIdlePerHost is { } maxIdlePerHost)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.MaxIdlePerHost)}' = {maxIdlePerHost}");
                NativeMethods.yaha_client_config_pool_max_idle_per_host(_ctx, (nuint)maxIdlePerHost);
            }
            if (settings.SkipCertificateVerification is { } skipCertificateVerification)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.SkipCertificateVerification)}' = {skipCertificateVerification}");
                NativeMethods.yaha_client_config_skip_certificate_verification(_ctx, skipCertificateVerification);
            }
            if (settings.RootCertificates is { } rootCertificates)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.RootCertificates)}' = Length:{rootCertificates.Length}");
                var rootCertificatesBytes = Encoding.UTF8.GetBytes(rootCertificates);
                fixed (byte* buffer = rootCertificatesBytes)
                {
                    var sb = new StringBuffer(buffer, rootCertificates.Length);
                    var validCertificatesCount = NativeMethods.yaha_client_config_add_root_certificates(_ctx, &sb);
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
                    NativeMethods.yaha_client_config_add_client_auth_key(_ctx, &sb);
                }
            }
            if (settings.ClientAuthCertificates is { } clientAuthCertificates)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.ClientAuthCertificates)}' = {clientAuthCertificates}");
                var strBytes = Encoding.UTF8.GetBytes(clientAuthCertificates);
                fixed (byte* buffer = strBytes)
                {
                    var sb = new StringBuffer(buffer, strBytes.Length);
                    NativeMethods.yaha_client_config_add_client_auth_certificates(_ctx, &sb);
                }
            }
            if (settings.Http2Only is { } http2Only)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2Only)}' = {http2Only}");
                NativeMethods.yaha_client_config_http2_only(_ctx, http2Only);
            }
            if (settings.Http2InitialStreamWindowSize is { } http2InitialStreamWindowSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2InitialStreamWindowSize)}' = {http2InitialStreamWindowSize}");
                NativeMethods.yaha_client_config_http2_initial_stream_window_size(_ctx, http2InitialStreamWindowSize);
            }
            if (settings.Http2InitialConnectionWindowSize is { } http2InitialConnectionWindowSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2InitialConnectionWindowSize)}' = {http2InitialConnectionWindowSize}");
                NativeMethods.yaha_client_config_http2_initial_connection_window_size(_ctx, http2InitialConnectionWindowSize);
            }
            if (settings.Http2AdaptiveWindow is { } http2AdaptiveWindow)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2AdaptiveWindow)}' = {http2AdaptiveWindow}");
                NativeMethods.yaha_client_config_http2_adaptive_window(_ctx, http2AdaptiveWindow);
            }
            if (settings.Http2MaxFrameSize is { } http2MaxFrameSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxFrameSize)}' = {http2MaxFrameSize}");
                NativeMethods.yaha_client_config_http2_max_frame_size(_ctx, http2MaxFrameSize);
            }
            if (settings.Http2KeepAliveInterval is { } http2KeepAliveInterval)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveInterval)}' = {http2KeepAliveInterval}");
                NativeMethods.yaha_client_config_http2_keep_alive_interval(_ctx, (ulong)http2KeepAliveInterval.TotalMilliseconds);
            }
            if (settings.Http2KeepAliveTimeout is { } http2KeepAliveTimeout)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveTimeout)}' = {http2KeepAliveTimeout}");
                NativeMethods.yaha_client_config_http2_keep_alive_timeout(_ctx, (ulong)http2KeepAliveTimeout.TotalMilliseconds);
            }
            if (settings.Http2KeepAliveWhileIdle is { } http2KeepAliveWhileIdle)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2KeepAliveWhileIdle)}' = {http2KeepAliveWhileIdle}");
                NativeMethods.yaha_client_config_http2_keep_alive_while_idle(_ctx, http2KeepAliveWhileIdle);
            }
            if (settings.Http2MaxConcurrentResetStreams is { } http2MaxConcurrentResetStreams)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxConcurrentResetStreams)}' = {http2MaxConcurrentResetStreams}");
                NativeMethods.yaha_client_config_http2_max_concurrent_reset_streams(_ctx, (nuint)http2MaxConcurrentResetStreams);
            }
            if (settings.Http2MaxSendBufferSize is { } http2MaxSendBufferSize)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Option '{nameof(settings.Http2MaxSendBufferSize)}' = {http2MaxSendBufferSize}");
                NativeMethods.yaha_client_config_http2_max_send_buf_size(_ctx, (nuint)http2MaxSendBufferSize);
            }

            NativeMethods.yaha_build_client(_ctx);

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
            var reqCtx = NativeMethods.yaha_request_new(_ctx, requestSequence);

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
                    ThrowHelper.VerifyPointer(_ctx, reqCtx);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_header(_ctx, reqCtx, &bufKey, &bufValue));
                }
            }

            // Set HTTP method
            {
                var strBytes = Encoding.UTF8.GetBytes(request.Method.ToString());
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    ThrowHelper.VerifyPointer(_ctx, reqCtx);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_method(_ctx, reqCtx, &buf));
                }
            }

            // Set URI
            {
                var strBytes = Encoding.UTF8.GetBytes(request.RequestUri.ToString());
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    ThrowHelper.VerifyPointer(_ctx, reqCtx);
                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_set_uri(_ctx, reqCtx, &buf));
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
            ThrowHelper.VerifyPointer(_ctx, reqCtx);
            ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_begin(_ctx, reqCtx));
            requestContextManaged.Start(); // NOTE: ReadRequestLoop must be started after `request_begin`.

            return requestContextManaged;
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

                ThrowHelper.VerifyPointer(requestContext._ctx, requestContext._requestContext);
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
                ThrowHelper.VerifyPointer(requestContext._ctx, requestContext._requestContext);
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
                    if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"Disposing {nameof(NativeHttpHandlerCore)}");
                    NativeMethods.yaha_dispose_runtime(_ctx);
                    _ctx = null;
                }
            }
        }
    }
}
