using System.Net;
using _YetAnotherHttpHandler.Test.Helpers;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class Http2ClearTextTest : UseTestServerTestBase
{
    public Http2ClearTextTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task Get_Ok()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.InsecureHttp2Only);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/")
        {
            Version = HttpVersion.Version20,
        };
        var response = await httpClient.SendAsync(request).WaitAsync(TimeoutToken);
        var responseBody = await response.Content.ReadAsStringAsync().WaitAsync(TimeoutToken);

        // Assert
        Assert.Equal("__OK__", responseBody);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_NotOk()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.InsecureHttp2Only);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/not-found")
        {
            Version = HttpVersion.Version20,
        };
        var response = await httpClient.SendAsync(request).WaitAsync(TimeoutToken);
        var responseBody = await response.Content.ReadAsStringAsync().WaitAsync(TimeoutToken);

        // Assert
        Assert.Equal("__Not_Found__", responseBody);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}