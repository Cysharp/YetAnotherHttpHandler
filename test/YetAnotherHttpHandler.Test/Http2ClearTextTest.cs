using Cysharp.Net.Http;
using Microsoft.AspNetCore.Builder;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class Http2ClearTextTest : Http2TestBase
{
    public Http2ClearTextTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    protected override YetAnotherHttpHandler CreateHandler()
    {
        return new YetAnotherHttpHandler() { Http2Only = true };
    }
    
    protected override Task<TestWebAppServer> LaunchServerAsyncCore<T>(Action<WebApplicationBuilder>? configure = null)
    {
        return LaunchServerAsync<T>(TestWebAppServerListenMode.InsecureHttp2Only, configure);
    }

}