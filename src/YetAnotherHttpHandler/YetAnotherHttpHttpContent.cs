using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Net.Http
{
    internal class YetAnotherHttpHttpContent : HttpContent
    {
        private readonly PipeReader _pipeReader;
        private readonly NativeLibraryWrapper.RequestContext _requestContext;

        internal YetAnotherHttpHttpContent(PipeReader pipeReader, NativeLibraryWrapper.RequestContext requestContext)
        {
            _pipeReader = pipeReader;
            _requestContext = requestContext;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, default);

#if NET5_0_OR_GREATER
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
#else
        protected async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
#endif
        {
            await _pipeReader.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            await _pipeReader.CompleteAsync().ConfigureAwait(false);

            _requestContext.Dispose();
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => CreateContentReadStreamAsync(default);

#if NET5_0_OR_GREATER
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
#else
        protected Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
#endif
        {
            return Task.FromResult<Stream>(new StreamWrapper(_requestContext, _pipeReader.AsStream()));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        private class StreamWrapper : Stream
        {
            private readonly NativeLibraryWrapper.RequestContext _requestContext;
            private readonly Stream _inner;

            public override bool CanRead => _inner.CanRead;

            public override bool CanSeek => _inner.CanSeek;

            public override bool CanWrite => _inner.CanWrite;

            public override long Length => _inner.Length;

            public override long Position { get => _inner.Position; set => _inner.Position = value; }

            public StreamWrapper(NativeLibraryWrapper.RequestContext requestContext, Stream inner)
            {
                _requestContext = requestContext;
                _inner = inner;
            }

            public override void Flush() => _inner.Flush();

            public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

#if !NETSTANDARD2_0
            public override int Read(Span<byte> buffer) => _inner.Read(buffer);
#endif

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);

#if !NETSTANDARD2_0
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
#endif

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

#if !NETSTANDARD2_0
            public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
#endif

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.WriteAsync(buffer, offset, count, cancellationToken);

#if !NETSTANDARD2_0
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _inner.WriteAsync(buffer, cancellationToken);
#endif

#if !NETSTANDARD2_0 && !UNITY_2019_1_OR_NEWER // WORKAROUND: If "Api Compatibility Level" is ".NET Framework" on Unity, some API facades are same as .NET Framework.
            public override void CopyTo(Stream destination, int bufferSize) => _inner.CopyTo(destination, bufferSize);
#endif

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => _inner.CopyToAsync(destination, bufferSize, cancellationToken);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _requestContext.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
