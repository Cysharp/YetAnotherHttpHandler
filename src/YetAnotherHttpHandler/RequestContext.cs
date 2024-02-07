using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
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
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly int _requestSequence;
        private readonly object _handleLock = new object();
        private readonly ManualResetEventSlim _fullyCompleted = new ManualResetEventSlim(false);
        private GCHandle _handle;

        internal YahaContextSafeHandle _ctxHandle;
        internal YahaRequestContextSafeHandle _requestContextHandle;

        private bool _requestBodyCompleted;
        private Task? _readRequestTask;
        private ResponseContext _response;

        public int RequestSequence => _requestSequence;
        public ResponseContext Response => _response;
        public PipeWriter Writer => _pipe.Writer;
        public IntPtr Handle => GCHandle.ToIntPtr(_handle);

        internal RequestContext(YahaContextSafeHandle ctx, YahaRequestContextSafeHandle requestContext, HttpRequestMessage requestMessage, int requestSequence, CancellationToken cancellationToken)
        {
            _ctxHandle = ctx;
            _requestContextHandle = requestContext;
            _response = new ResponseContext(requestMessage, this, cancellationToken);
            _readRequestTask = default;
            _requestSequence = requestSequence;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        internal void Start()
        {
            _readRequestTask = RunReadRequestLoopAsync(_cancellationTokenSource.Token);
        }

        public void Allocate()
        {
            Debug.Assert(!_handle.IsAllocated);
            lock (_handleLock)
            {
                _handle = GCHandle.Alloc(this);
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] State allocated");
            }
        }

        public void Release()
        {
            Debug.Assert(_handle.IsAllocated);
            lock (_handleLock)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Releasing state");
                _handle.Free();
                _handle = default;
                _fullyCompleted.Set();
            }
        }

        public static RequestContext FromHandle(IntPtr handle)
            => (RequestContext)(GCHandle.FromIntPtr(handle).Target ?? throw new InvalidOperationException());

        private async Task RunReadRequestLoopAsync(CancellationToken cancellationToken)
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Begin RunReadRequestLoopAsync");

            try
            {
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
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Completing RunReadRequestLoopAsync");
            }
        }

        private unsafe void WriteBody(Span<byte> data)
        {
            lock (_handleLock)
            {
                Debug.Assert(_handle.IsAllocated);

                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Sending the request body: Length={data.Length}");

                var addRefContext = false;
                var addRefReqContext = false;
                try
                {
                    _ctxHandle.DangerousAddRef(ref addRefContext);
                    _requestContextHandle.DangerousAddRef(ref addRefReqContext);

                    var ctx = _ctxHandle.DangerousGet();
                    var requestContext = _requestContextHandle.DangerousGet();

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
#if DEBUG
                    // Fill memory so that data corruption can be detected on debug build.
                    data.Fill(0xff);
#endif
                }
                finally
                {
                    if (addRefContext)
                    {
                        _ctxHandle.DangerousRelease();
                    }

                    if (addRefReqContext)
                    {
                        _requestContextHandle.DangerousRelease();
                    }
                }
            }
        }

        private unsafe void TryCompleteBody(Exception? exception = default)
        {
            lock (_handleLock)
            {
                if (!_handle.IsAllocated)
                {
                    // The request has already completed. (`on_complete` callback has been called).
                    return;
                }

                if (_ctxHandle.IsClosed || _requestContextHandle.IsClosed)
                {
                    // The request has already completed. We need to nothing to do at here.
                    return;
                }

                if (_requestBodyCompleted)
                {
                    return;
                }

                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Complete sending the request body{(exception is null ? string.Empty : "; Exception=" + exception.Message)}");

                var addRefContext = false;
                var addRefReqContext = false;
                try
                {
                    _ctxHandle.DangerousAddRef(ref addRefContext);
                    _requestContextHandle.DangerousAddRef(ref addRefReqContext);

                    var ctx = _ctxHandle.DangerousGet();
                    var requestContext = _requestContextHandle.DangerousGet();

                    ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_complete_body(ctx, requestContext));
                }
                finally
                {
                    if (addRefContext)
                    {
                        _ctxHandle.DangerousRelease();
                    }
                    if (addRefReqContext)
                    {
                        _requestContextHandle.DangerousRelease();
                    }
                }
                
                _pipe.Reader.Complete(exception);

                _requestBodyCompleted = true;
            }
        }

        public unsafe bool TryAbort()
        {
            lock (_handleLock)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}:State:0x{Handle:X}] Try to abort the request");
                if (!_handle.IsAllocated)
                {
                    // If the handle has been already released, the request is fully completed.
                    return false;
                }

                _cancellationTokenSource.Cancel();

                var addRefContextHandle = false;
                var addRefRequestContextHandle = false;
                try
                {
                    _ctxHandle.DangerousAddRef(ref addRefContextHandle);
                    _requestContextHandle.DangerousAddRef(ref addRefRequestContextHandle);

                    NativeMethods.yaha_request_abort(_ctxHandle.DangerousGet(), _requestContextHandle.DangerousGet());
                }
                finally
                {
                    if (addRefContextHandle)
                    {
                        _ctxHandle.DangerousRelease();
                    }
                    if (addRefRequestContextHandle)
                    {
                        _requestContextHandle.DangerousRelease();
                    }
                }
                return true;
            }
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

            // Abort the request and dispose the request context handle whether called from manual Dispose or the finalizer.
            TryAbort();
            _requestContextHandle.Dispose();

            if (disposing)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                // DO NOT Dispose `_ctx` here.
            }
            else
            {
                // Executing within the finalizer thread.
                // NOTE: Waits by blocking until the request is completed on the native side.
                //       If not waited here, issues such as crashes may occur when callbacks are invoked after the .NET side is destroyed by Unity's Domain Reload.
                //       However, caution is needed with the invocation order and timing of callbacks, as well as the handling of locks, since the finalizer thread may become blocked.
                _fullyCompleted.Wait();
            }
        }
    }
}