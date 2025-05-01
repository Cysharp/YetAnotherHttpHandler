using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Cysharp.Net.Http
{
    internal static class ThrowHelper
    {
        [Conditional("__VERIFY_POINTER")]
        public static unsafe void VerifyPointer(YahaNativeContext* ctx, YahaNativeRequestContext* reqCtx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (reqCtx == null) throw new ArgumentNullException(nameof(reqCtx));
        }

        [Conditional("__VERIFY_POINTER")]
        public static unsafe void VerifyPointer(YahaContextSafeHandle ctx, YahaRequestContextSafeHandle reqCtx)
        {
            if (ctx.IsInvalid) throw new ArgumentNullException(nameof(ctx));
            if (reqCtx.IsInvalid) throw new ArgumentNullException(nameof(reqCtx));
        }

#if NET6_0_OR_GREATER
        [DoesNotReturn]
#endif
        public static void ThrowOperationCanceledException()
            => throw new OperationCanceledException();

#if NETSTANDARD2_0
        public static unsafe void ThrowIfFailed(YahaNativeContext* ctx, YahaNativeRequestContext* reqCtx, bool result)
#else
        public static unsafe void ThrowIfFailed(YahaNativeContext* ctx, YahaNativeRequestContext* reqCtx, [DoesNotReturnIf(false)]bool result)
#endif
        {
            if (!result)
            {
                var buf = NativeMethods.yaha_get_last_error(ctx, reqCtx);
                if (buf != null)
                {
                    try
                    {
                        throw new InvalidOperationException(UnsafeUtilities.GetStringFromUtf8Bytes(buf->AsSpan()));
                    }
                    finally
                    {
                        NativeMethods.yaha_free_byte_buffer(buf);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unexpected error occurred.");
                }
            }
        }
    }
}