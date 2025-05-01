using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class NativeMethodsTest
{
    [Fact]
    public unsafe void GetLastError_Empty()
    {
        var runtimeHandle = NativeRuntime.Instance.Acquire();
        try
        {
            var ctx = NativeMethods.yaha_init_context(runtimeHandle.DangerousGet(), null, null, null);
            var reqCtx = NativeMethods.yaha_request_new(ctx, 0);

            var buf = NativeMethods.yaha_get_last_error(ctx, reqCtx);
            if (buf != null)
            {
                NativeMethods.yaha_free_byte_buffer(buf);
            }

            NativeMethods.yaha_request_destroy(ctx, reqCtx);
            NativeMethods.yaha_dispose_context(ctx);
        }
        finally
        {
            NativeRuntime.Instance.Release();
        }
    }
}