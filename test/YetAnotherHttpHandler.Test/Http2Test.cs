using Cysharp.Net.Http;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

[OSSkipCondition(OperatingSystems.MacOSX)] // .NET 7 or earlier does not support ALPN on macOS.
public class Http2Test : Http2TestBase
{
    public Http2Test(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    protected override HttpMessageHandler CreateHandler()
    {
        return new YetAnotherHttpHandler();
    }
}