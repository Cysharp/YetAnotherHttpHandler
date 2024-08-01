using System.Net;
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

    [Fact]
    public async Task FailedToConnect_VersionMismatch()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler() { Http2Only = false };
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

        // Act
        var ex = await Record.ExceptionAsync(async () => (await httpClient.GetAsync($"{server.BaseUri}/")).EnsureSuccessStatusCode());

        // Assert
        Assert.IsType<HttpRequestException>(ex);
        Assert.Equal(HttpStatusCode.BadRequest, ((HttpRequestException)ex).StatusCode);
    }
}