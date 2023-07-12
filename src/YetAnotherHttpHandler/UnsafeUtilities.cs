using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cysharp.Net.Http
{
    internal static class UnsafeUtilities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static string GetStringFromUtf8Bytes(ReadOnlySpan<byte> bytes)
        {
#if !NETSTANDARD2_0
            return Encoding.UTF8.GetString(bytes);
#else
            fixed (byte* p = bytes)
            {
                return Encoding.UTF8.GetString(p, bytes.Length);
            }
#endif
        }
    }
}
