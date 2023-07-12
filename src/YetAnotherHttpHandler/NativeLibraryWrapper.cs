using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    internal class NativeLibraryWrapper : IDisposable
    {
        private unsafe YahaNativeContext* _ctx;
        private static ConcurrentDictionary<int, RequestContext> _inflightRequests = new ConcurrentDictionary<int, RequestContext>();
        private static int _requestSequence = 0;

        public unsafe NativeLibraryWrapper()
        {
            //Console.WriteLine("init_runtime");
#if FALSE
            _ctx = NativeMethods.yaha_init_runtime(&OnStatusCodeAndHeaderReceive, &OnReceive, &OnComplete);
#else
            _ctx = NativeMethods.yaha_init_runtime(OnStatusCodeAndHeaderReceive, OnReceive, OnComplete);
#endif
        }

        public unsafe RequestContext Send(HttpRequestMessage request)
        {
            //Console.WriteLine($"begin_request: Begin ({url})");

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

                fixed (byte* pKey =  keyBytes)
                fixed (byte* pValue = valueBytes)
                {
                    var bufKey = new StringBuffer(pKey, keyBytes.Length);
                    var bufValue = new StringBuffer(pValue, valueBytes.Length);
                    VerifyPointer(_ctx, reqCtx);
                    ThrowIfFailed(_ctx, reqCtx, NativeMethods.yaha_request_set_header(_ctx, reqCtx, &bufKey, &bufValue));
                }
            }

            // Set HTTP method
            {
                var strBytes = Encoding.UTF8.GetBytes(request.Method.ToString());
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    VerifyPointer(_ctx, reqCtx);
                    ThrowIfFailed(_ctx, reqCtx, NativeMethods.yaha_request_set_method(_ctx, reqCtx, &buf));
                }
            }

            // Set URI
            {
                var strBytes = Encoding.UTF8.GetBytes(request.RequestUri.ToString());
                fixed (byte* p = strBytes)
                {
                    var buf = new StringBuffer(p, strBytes.Length);
                    VerifyPointer(_ctx, reqCtx);
                    ThrowIfFailed(_ctx, reqCtx, NativeMethods.yaha_request_set_uri(_ctx, reqCtx, &buf));
                }
            }
            
            // Set HTTP version
            YahaHttpVersion version;
            if (request.Version == HttpVersion.Version10)
            {
                version = YahaHttpVersion.Http10;
            }
            else if (request.Version == HttpVersion.Version11)
            {
                version = YahaHttpVersion.Http11;
            }
            else if (request.Version == HttpVersionShim.Version20)
            {
                version = YahaHttpVersion.Http2;
            }
            else
            {
                throw new NotSupportedException($"Unsupported HTTP version '{request.Version}'");
            }
            NativeMethods.yaha_request_set_version(_ctx, reqCtx, version);

            // Prepare body channel
            NativeMethods.yaha_request_set_has_body(_ctx, reqCtx, request.Content != null);

            // Begin request
            var requestContextManaged = new RequestContext(_ctx, reqCtx);
            _inflightRequests[requestSequence] = requestContextManaged;

            VerifyPointer(_ctx, reqCtx);
            ThrowIfFailed(_ctx, reqCtx, NativeMethods.yaha_request_begin(_ctx, reqCtx));
            requestContextManaged.Start(); // NOTE: ReadRequestLoop must be started after `request_begin`.

            return requestContextManaged;
        }

        [Conditional("__VERIFY_POINTER")]
        private unsafe static void VerifyPointer(YahaNativeContext* ctx, YahaNativeRequestContext* reqCtx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (reqCtx == null) throw new ArgumentNullException(nameof(reqCtx));
        }

        private unsafe static void ThrowIfFailed(YahaNativeContext* ctx, YahaNativeRequestContext* reqCtx, bool result)
        {
            if (!result)
            {
                VerifyPointer(ctx, reqCtx);
                NativeMethods.yaha_request_get_last_error(ctx, reqCtx);
                throw new InvalidOperationException("Something went wrong.");
            }
        }
        

        public class RequestContext : IDisposable
        {
            private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);

            internal unsafe YahaNativeContext* _ctx;
            internal unsafe YahaNativeRequestContext* _requestContext;

            private Task _readRequestTask;
            private Response _response;

            public Response Response => _response;
            public PipeWriter Writer => _pipe.Writer;

            internal unsafe RequestContext(YahaNativeContext* ctx, YahaNativeRequestContext* requestContext)
            {
                _ctx = ctx;
                _requestContext = requestContext;
                _response = new Response(this);
                _readRequestTask = default;
            }

            internal void Start()
            {
                _readRequestTask = RunReadRequestLoopAsync(default);
            }

            private async Task RunReadRequestLoopAsync(CancellationToken cancellationToken)
            {
                while (true)
                {
                    var result = await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                    if (result.Buffer.Length > 0)
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent((int)result.Buffer.Length);
                        try
                        {
                            result.Buffer.CopyTo(buffer);
                            Write(buffer.AsSpan(0, (int)result.Buffer.Length));
                            _pipe.Reader.AdvanceTo(result.Buffer.End);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    if (result.IsCompleted || result.IsCanceled) break;
                }
                Complete();
            }

            public unsafe void Write(Span<byte> data)
            {
                //Console.WriteLine($"write_body: Begin (Length={data.Length})");
                VerifyPointer(_ctx, _requestContext);
                ThrowIfFailed(_ctx, _requestContext, NativeMethods.yaha_request_write_body(_ctx, _requestContext, (byte*)Unsafe.AsPointer(ref data.GetPinnableReference()), (UIntPtr)data.Length));
                //Console.WriteLine($"write_body: End ({result})");
            }

            public unsafe void Complete()
            {
                //Console.WriteLine("complete_body: Begin");
                if (_ctx == null || _requestContext == null)
                {
                    // The request has already completed. We need to nothing to do at here.
                    return;
                }

                VerifyPointer(_ctx, _requestContext);
                ThrowIfFailed(_ctx, _requestContext, NativeMethods.yaha_request_complete_body(_ctx, _requestContext));
                //Console.WriteLine("complete_body: End");
            }

            public unsafe void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            ~RequestContext()
            {
                Dispose(false);
            }

            private unsafe void Dispose(bool disposing)
            {
                if (_requestContext != null)
                {
                    VerifyPointer(_ctx, _requestContext);
                    ThrowIfFailed(_ctx, _requestContext, NativeMethods.yaha_request_destroy(_ctx, _requestContext));
                    _requestContext = null;
                }

                if (disposing)
                {
                    _ctx = null;
                }
            }
        }

        public class Response
        {
            private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);
            private readonly TaskCompletionSource<HttpResponseMessage> _responseTask;
            private readonly HttpResponseMessage _message;

            public PipeReader Reader => _pipe.Reader;

            internal Response(RequestContext requestContext)
            {
                _responseTask = new TaskCompletionSource<HttpResponseMessage>();
                _message = new HttpResponseMessage()
                {
                    Content = new YetAnotherHttpHttpContent(Reader, requestContext),
                    Version = HttpVersion.Version10,
                };
            }

            public void Write(ReadOnlySpan<byte> data)
            {
                //Console.WriteLine($"Response.Write: {data.Length} bytes");
                //Console.WriteLine("Response.Write: " + Encoding.UTF8.GetString(data));
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
                //Console.WriteLine($"Response.SetHeader: {name} = {value}");
                if (!_message.Headers.TryAddWithoutValidation(name, value))
                {
                    _message.Content.Headers.TryAddWithoutValidation(name, value);
                }
                //_message.Content.Headers.TryAddWithoutValidation(name, value);
            }
            
            public void SetHeader(ReadOnlySpan<byte> nameBytes, ReadOnlySpan<byte> valueBytes)
            {
                var name = UnsafeUtilities.GetStringFromUtf8Bytes(nameBytes);
                var value = UnsafeUtilities.GetStringFromUtf8Bytes(valueBytes);

                //Console.WriteLine($"Response.SetHeader: {name} = {value}");
                if (!_message.Headers.TryAddWithoutValidation(name, value))
                {
                    _message.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            internal void SetVersion(YahaHttpVersion version)
            {
                switch (version)
                {
                    case YahaHttpVersion.Http10: _message.Version = HttpVersion.Version10; break;
                    case YahaHttpVersion.Http11: _message.Version = HttpVersion.Version11; break;
                    case YahaHttpVersion.Http2: _message.Version = HttpVersionShim.Version20; break;
                    default: _message.Version = HttpVersionShim.Unknown; break;
                }
            }

            public void SetTrailer(string name, string value)
            {
                //Console.WriteLine($"Response.SetTrailer: {name} = {value}");
                _message.TrailingHeaders().TryAddWithoutValidation(name, value);
            }

            public void SetTrailer(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
            {
                //Console.WriteLine($"Response.SetTrailer: {name} = {value}");
                _message.TrailingHeaders().TryAddWithoutValidation(UnsafeUtilities.GetStringFromUtf8Bytes(name), UnsafeUtilities.GetStringFromUtf8Bytes(value));
            }

            public void SetStatusCode(int statusCode)
            {
                //Console.WriteLine($"Response.SetStatusCode: {statusCode}");
                _message.StatusCode = (HttpStatusCode)statusCode;
                _responseTask.SetResult(_message);
            }

            public void Complete()
            {
                //Console.WriteLine("Response.Complete");
                _pipe.Writer.Complete();
            }

            public void CompleteAsFailed(string errorMessage)
            {
                //Console.WriteLine("Response.Complete");
                _pipe.Writer.Complete();
                _responseTask.SetException(new HttpRequestException(errorMessage));
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
        
    #if FALSE
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    #endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_runtime_on_status_code_and_headers_receive_delegate))]
        private static unsafe void OnStatusCodeAndHeaderReceive(int reqSeq, int statusCode, YahaHttpVersion version)
        {
            if (_inflightRequests.TryGetValue(reqSeq, out var requestContext))
            {
                requestContext.Response.SetVersion(version);

                VerifyPointer(requestContext._ctx, requestContext._requestContext);
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
                            NativeMethods.yaha_request_free_byte_buffer(bufKey);
                            NativeMethods.yaha_request_free_byte_buffer(bufValue);
                        }
                    }
                }
                requestContext.Response.SetStatusCode(statusCode);
            }
        }

    #if FALSE
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    #endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_runtime_on_receive_delegate))]
        private static unsafe void OnReceive(int reqSeq, UIntPtr length, byte* buf)
        {
            var bufSpan = new Span<byte>(buf, (int)length);
            _inflightRequests[reqSeq].Response.Write(bufSpan);
        }

    #if FALSE
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    #endif
        [MonoPInvokeCallback(typeof(NativeMethods.yaha_init_runtime_on_complete_delegate))]
        private static unsafe void OnComplete(int reqSeq, byte hasError)
        {
            if (_inflightRequests.TryGetValue(reqSeq, out var requestContext))
            {
                VerifyPointer(requestContext._ctx, requestContext._requestContext);
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
                                NativeMethods.yaha_request_free_byte_buffer(bufKey);
                                NativeMethods.yaha_request_free_byte_buffer(bufValue);
                            }
                        }
                    }

                    requestContext.Response.Complete();
                }
                else
                {
                    var buf = NativeMethods.yaha_request_get_last_error(requestContext._ctx, requestContext._requestContext);
                    try
                    {
#if !NETSTANDARD2_0
                        requestContext.Response.CompleteAsFailed(Encoding.UTF8.GetString(buf->AsSpan()));
#else
                        requestContext.Response.CompleteAsFailed(Encoding.UTF8.GetString(buf->ptr, buf->length));
#endif
                    }
                    catch
                    {
                        NativeMethods.yaha_request_free_byte_buffer(buf);
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
                    //Console.WriteLine("dispose_runtime");
                    NativeMethods.yaha_dispose_runtime(_ctx);
                    _ctx = null;
                }
            }
        }
    }
}
