using System.Net;
using _YetAnotherHttpHandler.Test.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class Http1Test : UseTestServerTestBase
{
    public Http1Test(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task Ok()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await TestWebAppServer.LaunchAsync<Http1TestServer>(TestWebAppServerListenMode.InsecureHttp1Only, TestOutputHelper);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }

    [Fact]
    public async Task NotOk()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await TestWebAppServer.LaunchAsync<Http1TestServer>(TestWebAppServerListenMode.InsecureHttp1Only, TestOutputHelper);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/not-found");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("__Not_Found__", responseBody);
    }

    [Fact]
    public async Task ResponseHeaders()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await TestWebAppServer.LaunchAsync<Http1TestServer>(TestWebAppServerListenMode.InsecureHttp1Only, TestOutputHelper);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/response-headers");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(new string[] {"foo"}, response.Headers.GetValues("x-test"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }

    class Http1TestServer : ITestServerBuilder
    {
        public static WebApplication BuildApplication(WebApplicationBuilder builder)
        {
            var app = builder.Build();

            app.MapGet("/", () => Results.Content("__OK__"));
            app.MapGet("/not-found", () => Results.Content("__Not_Found__", statusCode: 404));
            app.MapGet("/response-headers", (HttpContext ctx) =>
            {
                ctx.Response.Headers["x-test"] = "foo";
                return Results.Content("__OK__");
            });

            return app;
        }
    }
}