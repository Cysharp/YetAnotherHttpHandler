using Cysharp.Net.Http;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class Http2ClearTextTest : Http2TestBase
{
    public Http2ClearTextTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    protected override HttpMessageHandler CreateHandler()
    {
        return new YetAnotherHttpHandler() { Http2Only = true };
    }
}