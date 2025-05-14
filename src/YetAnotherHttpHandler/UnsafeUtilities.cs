using System;
using System.Buffers;
using System.Diagnostics;
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

        [Conditional("DEBUG")]
        public static void RequireRunningOnManagedThread()
        {
            // NOTE: This check logic is working only on Windows.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var threadName = GetCurrentThreadName();
            if (threadName == "tokio-runtime-worker")
            {
                Environment.FailFast($"The current thread is the tokio worker thread.");
            }

            static string GetCurrentThreadName()
            {
                const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;

                var threadId = GetCurrentThreadId();
                var threadName = string.Empty;
                var threadHandle = OpenThread(THREAD_QUERY_LIMITED_INFORMATION, false, threadId);

                if (threadHandle != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr threadDescriptionPtr;
                        var result = GetThreadDescription(threadHandle, out threadDescriptionPtr);

                        if (result >= 0 && threadDescriptionPtr != IntPtr.Zero)
                        {
                            try
                            {
                                threadName = Marshal.PtrToStringUni(threadDescriptionPtr);
                            }
                            finally
                            {
                                LocalFree(threadDescriptionPtr);
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(threadHandle);
                    }
                }

                return threadName ?? string.Empty;

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern bool CloseHandle(IntPtr hObject);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern uint GetCurrentThreadId();

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

                [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                static extern int GetThreadDescription(IntPtr hThread, out IntPtr ppszThreadDescription);

                [DllImport("kernel32.dll")]
                static extern IntPtr LocalFree(IntPtr hMem);
            }
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
