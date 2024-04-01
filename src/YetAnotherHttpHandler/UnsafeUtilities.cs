using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

        // System.Text.Ascii.EqualsIgnoreCase
        public static bool EqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
            => left.Length == right.Length && EqualsIgnoreCase(ref MemoryMarshal.GetReference(left), ref MemoryMarshal.GetReference(right), (uint)left.Length);
        public static bool EqualsIgnoreCase(ref byte left, ref byte right, uint length)
        {
            for (nuint i = 0; i < length; ++i)
            {
                uint valueA = unchecked((uint)(Unsafe.Add(ref left, (nint)i)));
                uint valueB = unchecked((uint)(Unsafe.Add(ref right, (nint)i)));

                if (!IsAsciiCodePoint(valueA | valueB))
                {
                    return false;
                }

                if (valueA == valueB)
                {
                    continue; // exact match
                }

                valueA |= 0x20u;
                if (valueA - 'a' > 'z' - 'a')
                {
                    return false; // not exact match, and first input isn't in [A-Za-z]
                }

                if (valueA != (valueB | 0x20u))
                {
                    return false;
                }
            }

            return true;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool IsAsciiCodePoint(uint value) => value <= 0x7Fu;
        }
    }

    internal readonly ref struct TempUtf8String
    {
        private readonly byte[]? _buffer;
        private readonly ReadOnlySpan<byte> _span;

        public ReadOnlySpan<byte> Span => _span;

        public TempUtf8String(string s)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(s.Length));
#if NETSTANDARD2_0
            var written = Encoding.UTF8.GetBytes(s, 0, s.Length, _buffer, 0);
#else
            var written = Encoding.UTF8.GetBytes(s, _buffer.AsSpan());
#endif
            _span = _buffer.AsSpan(0, written);
        }

        public TempUtf8String(ReadOnlySpan<byte> s)
        {
            _buffer = null;
            _span = s;
        }

        public void Dispose()
        {
            if (_buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }
    }
}
