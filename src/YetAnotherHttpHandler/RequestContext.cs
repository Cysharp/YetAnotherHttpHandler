using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    internal class RequestContext : IDisposable
    {
        private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);
        private readonly CancellationToken _cancellationToken;
        private readonly int _requestSequence;
        private GCHandle _handle;

        internal YahaContextSafeHandle _ctx;
        internal YahaRequestContextSafeHandle _requestContext;

        private bool _requestBodyCompleted;
        private Task? _readRequestTask;
        private ResponseContext _response;

        public int RequestSequence => _requestSequence;
        public ResponseContext Response => _response;
        public PipeWriter Writer => _pipe.Writer;
        public IntPtr Handle => GCHandle.ToIntPtr(_handle);

        internal RequestContext(YahaContextSafeHandle ctx, YahaRequestContextSafeHandle requestContext, HttpRequestMessage requestMessage, int requestSequence, CancellationToken cancellationToken)
        {
            _ctx = ctx;
            _requestContext = requestContext;
            _response = new ResponseContext(requestMessage, this, cancellationToken);
            _readRequestTask = default;
            _requestSequence = requestSequence;
            _cancellationToken = cancellationToken;
        }

        internal void Start()
        {
            _readRequestTask = RunReadRequestLoopAsync(_cancellationToken);
        }

        public void Allocate()
        {
            Debug.Assert(!_handle.IsAllocated);
            _handle = GCHandle.Alloc(this);
        }

        public void Release()
        {
            Debug.Assert(_handle.IsAllocated);
            _handle.Free();
            _handle = default;
        }

        public static RequestContext FromHandle(IntPtr handle)
            => (RequestContext)GCHandle.FromIntPtr(handle).Target ?? throw new InvalidOperationException();

        private async Task RunReadRequestLoopAsync(CancellationToken cancellationToken)
        {
            var addRefContext = false;
            var addRefReqContext = false;
            try
            {
                _ctx.DangerousAddRef(ref addRefContext);
                _requestContext.DangerousAddRef(ref addRefReqContext);

                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                        if (result.Buffer.Length > 0)
                        {
                            var buffer = ArrayPool<byte>.Shared.Rent((int)result.Buffer.Length);
                            try
                            {
                                result.Buffer.CopyTo(buffer);
                                WriteBody(buffer.AsSpan(0, (int)result.Buffer.Length));
                                _pipe.Reader.AdvanceTo(result.Buffer.End);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }

                        if (result.IsCompleted || result.IsCanceled) break;
                    }

                    TryCompleteBody();
                }
                catch (Exception e)
                {
                    TryCompleteBody(e);
                }
            }
            finally
            {
                if (addRefContext)
                {
                    _ctx.DangerousRelease();
                }

                if (addRefReqContext)
                {
                    _requestContext.DangerousRelease();
                }
            }
        }

        private unsafe void WriteBody(Span<byte> data)
        {
            Debug.Assert(_handle.IsAllocated);

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Sending the request body: Length={data.Length}");
            ThrowHelper.VerifyPointer(_ctx, _requestContext);

            // DangerousAddRef/Release are already called by caller.
            var ctx = _ctx.DangerousGet();
            var requestContext = _requestContext.DangerousGet();

            while (!_requestBodyCompleted)
            {
                // If the internal buffer is full, yaha_request_write_body returns false. We need to wait until ready to send bytes again.
                if (NativeMethods.yaha_request_write_body(ctx, requestContext, (byte*)Unsafe.AsPointer(ref data.GetPinnableReference()), (UIntPtr)data.Length))
                {
                    break;
                }
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Send buffer is full.");

                // TODO:
                Thread.Sleep(10);
            }
        }

        private unsafe void TryCompleteBody(Exception? exception = default)
        {
            Debug.Assert(_handle.IsAllocated);

            if (_ctx.IsClosed || _requestContext.IsClosed)
            {
                // The request has already completed. We need to nothing to do at here.
                return;
            }

            if (_requestBodyCompleted)
            {
                return;
            }

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Complete sending the request body{(exception is null ? string.Empty : "; Exception=" + exception.Message)}");
            ThrowHelper.VerifyPointer(_ctx, _requestContext);
            
            var addRefContext = false;
            var addRefReqContext = false;
            try
            {
                _ctx.DangerousAddRef(ref addRefContext);
                _requestContext.DangerousAddRef(ref addRefReqContext);

                var ctx = _ctx.DangerousGet();
                var requestContext = _requestContext.DangerousGet();

                ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_complete_body(ctx, requestContext));
            }
            finally
            {
                if (addRefContext)
                {
                    _ctx.DangerousRelease();
                }
                if (addRefReqContext)
                {
                    _requestContext.DangerousRelease();
                }
            }
            
            _pipe.Reader.Complete(exception);

            _requestBodyCompleted = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~RequestContext()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Dispose RequestContext: disposing={disposing}");

            if (disposing)
            {
                _requestContext.Dispose();
                // DO NOT Dispose `_ctx` here.
            }
        }
    }
}