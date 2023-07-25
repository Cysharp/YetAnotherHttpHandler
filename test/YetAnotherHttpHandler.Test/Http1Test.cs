using System.Net;
using System.Runtime.ExceptionServices;
using _YetAnotherHttpHandler.Test.Helpers;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace _YetAnotherHttpHandler.Test;

public class Http1Test : UseTestServerTestBase
{
    public Http1Test(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task FailedToConnect()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var ex = await Record.ExceptionAsync(async () => await httpClient.GetAsync($"http://localhost.exmample/"));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
    }

    [Fact]
    public async Task Get_Ok()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }

    [Fact]
    public async Task Get_NotOk()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/not-found");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("__Not_Found__", responseBody);
    }

    [Fact]
    public async Task Get_ResponseHeaders()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/response-headers");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(new string[] {"foo"}, response.Headers.GetValues("x-test"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }
}