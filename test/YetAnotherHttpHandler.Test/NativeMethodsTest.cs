using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class NativeMethodsTest
{
    [Fact]
    public async Task InvalidRequestHeaderValue_ReturnsManagedError()
    {
        using var handler = new YetAnotherHttpHandler();
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/");
        Assert.True(request.Headers.TryAddWithoutValidation("X-Untrusted", "safe\r\nInjected: yes"));

        var exception = await Record.ExceptionAsync(() => client.SendAsync(request));

        var invalidHeader = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Invalid HTTP header value", invalidHeader.Message);
    }

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
