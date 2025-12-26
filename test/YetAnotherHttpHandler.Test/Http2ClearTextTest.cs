using System.Net;
using Cysharp.Net.Http;
using HttpClientTestServer;

namespace _YetAnotherHttpHandler.Test;

public class Http2ClearTextTest(ITestOutputHelper testOutputHelper) : Http2TestBase(testOutputHelper)
{
    protected override YetAnotherHttpHandler CreateHandler()
    {
        return new YetAnotherHttpHandler() { Http2Only = true };
    }

    protected override Task<ITestServer> LaunchServerAsync()
        => LaunchServerAsync(new TestServerOptions(ListenHttpProtocols.Http2, isSecure: false));

    [Fact]
    public async Task FailedToConnect_VersionMismatch()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler() { Http2Only = false };
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync();

        // Act
        var ex = await Record.ExceptionAsync(async () => (await httpClient.GetAsync($"{server.BaseUri}/")).EnsureSuccessStatusCode());

        // Assert
        Assert.IsType<HttpRequestException>(ex);
        Assert.Equal(HttpStatusCode.BadRequest, ((HttpRequestException)ex).StatusCode);
    }
}
