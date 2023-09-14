using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    internal class RequestContext : IDisposable
    {
        private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);
        private readonly CancellationToken _cancellationToken;
        private readonly int _requestSequence;

        internal unsafe YahaNativeContext* _ctx;
        internal unsafe YahaNativeRequestContext* _requestContext;

        private bool _requestBodyCompleted;
        private Task? _readRequestTask;
        private ResponseContext _response;

        public int RequestSequence => _requestSequence;
        public ResponseContext Response => _response;
        public PipeWriter Writer => _pipe.Writer;

        internal unsafe RequestContext(YahaNativeContext* ctx, YahaNativeRequestContext* requestContext, HttpRequestMessage requestMessage, int requestSequence, CancellationToken cancellationToken)
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

        private async Task RunReadRequestLoopAsync(CancellationToken cancellationToken)
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

                TryComplete();
            }
            catch (Exception e)
            {
                TryComplete(e);
            }

            unsafe void Write(Span<byte> data)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}] Sending the request body: Length={data.Length}");
                ThrowHelper.VerifyPointer(_ctx, _requestContext);

                while (!_requestBodyCompleted)
                {
                    // If the internal buffer is full, yaha_request_write_body returns false. We need to wait until ready to send bytes again.
                    if (NativeMethods.yaha_request_write_body(_ctx, _requestContext, (byte*)Unsafe.AsPointer(ref data.GetPinnableReference()), (UIntPtr)data.Length))
                    {
                        break;
                    }
                    if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Trace($"[ReqSeq:{_requestSequence}] Send buffer is full.");

                    // TODO:
                    Thread.Sleep(10);
                }
            }
        }

        public unsafe void TryComplete(Exception? exception = default)
        {
            if (_ctx == null || _requestContext == null)
            {
                // The request has already completed. We need to nothing to do at here.
                return;
            }

            if (_requestBodyCompleted)
            {
                return;
            }

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}] Complete sending the request body{(exception is null ? string.Empty : "; Exception=" + exception.Message)}");

            ThrowHelper.VerifyPointer(_ctx, _requestContext);
            ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_complete_body(_ctx, _requestContext));
            
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

        private unsafe void Dispose(bool disposing)
        {
            if (_requestContext != null)
            {
                if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}] Disposing RequestContext");
                ThrowHelper.VerifyPointer(_ctx, _requestContext);
                ThrowHelper.ThrowIfFailed(NativeMethods.yaha_request_destroy(_ctx, _requestContext));
                _requestContext = null;
            }

            if (disposing)
            {
                _ctx = null;
            }
        }
    }
}