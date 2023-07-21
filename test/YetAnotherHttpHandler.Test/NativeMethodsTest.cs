using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class NativeMethodsTest
{
    [Fact]
    public unsafe void GetLastError_Empty()
    {
        var buf = NativeMethods.yaha_get_last_error();
        if (buf != null)
        {
            NativeMethods.yaha_free_byte_buffer(buf);
        }
    }
}