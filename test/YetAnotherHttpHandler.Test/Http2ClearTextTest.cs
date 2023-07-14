using System.Net;
using _YetAnotherHttpHandler.Test.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class Http2ClearTextTest : UseTestServerTestBase
{
    public Http2ClearTextTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task Ok()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await TestWebAppServer.LaunchAsync<Http2TestServer>(TestWebAppServerListenMode.InsecureHttp2Only, TestOutputHelper);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal("__OK__", responseBody);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NotOk()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await TestWebAppServer.LaunchAsync<Http2TestServer>(TestWebAppServerListenMode.InsecureHttp2Only, TestOutputHelper);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/not-found");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal("__Not_Found__", responseBody);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    class Http2TestServer : ITestServerBuilder
    {
        public static WebApplication BuildApplication(WebApplicationBuilder builder)
        {
            var app = builder.Build();

            app.MapGet("/", () => Results.Content("__OK__"));
            app.MapGet("/not-found", () => Results.Content("__Not_Found__", statusCode: 404));

            return app;
        }
    }
}