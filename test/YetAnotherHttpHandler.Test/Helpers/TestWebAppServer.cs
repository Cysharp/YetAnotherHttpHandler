using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TestWebApp;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test.Helpers;

public class TestWebAppServer : IDisposable
{
    private readonly Task _appTask;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly TaskCompletionSource _waitForAppStarted;

    public int Port { get; }
    public bool IsSecure { get; }

    public string BaseUri => $"{(IsSecure ? "https" : "http")}://localhost:{Port}";

    private TestWebAppServer(int port, TestWebAppServerListenMode listenMode, ITestOutputHelper? testOutputHelper, Func<WebApplicationBuilder, WebApplication> webAppBuilder)
    {
        Port = port;
        IsSecure = listenMode is TestWebAppServerListenMode.SecureHttp1Only or
            TestWebAppServerListenMode.SecureHttp2Only or
            TestWebAppServerListenMode.SecureHttp1AndHttp2;

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(port, listenOptions =>
            {
                listenOptions.Protocols = listenMode switch
                {
                    TestWebAppServerListenMode.InsecureHttp1Only => HttpProtocols.Http1,
                    TestWebAppServerListenMode.InsecureHttp2Only => HttpProtocols.Http2,
                    TestWebAppServerListenMode.SecureHttp1Only => HttpProtocols.Http1,
                    TestWebAppServerListenMode.SecureHttp2Only => HttpProtocols.Http2,
                    TestWebAppServerListenMode.SecureHttp1AndHttp2 => HttpProtocols.Http1AndHttp2,
                    _ => throw new NotSupportedException(),
                };

                if (IsSecure)
                {
                    listenOptions.UseHttps();
                }
            });
        });
        if (testOutputHelper is not null)
        {
            builder.Logging.AddProvider(new TestOutputLoggerProvider(testOutputHelper));
        }

        var app = webAppBuilder(builder);

        _waitForAppStarted = new TaskCompletionSource();
        _appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        _appLifetime.ApplicationStarted.Register(() => _waitForAppStarted.SetResult());
        _appTask = app.RunAsync();
    }

    public static async Task<TestWebAppServer> LaunchAsync<T>(TestWebAppServerListenMode listenMode, ITestOutputHelper? testOutputHelper = null, CancellationToken shutdownToken = default)
        where T : ITestServerBuilder
    {
        var port = TestServerHelper.GetUnusedEphemeralPort();
        var server = new TestWebAppServer(port, listenMode, testOutputHelper, T.BuildApplication);
        await server._waitForAppStarted.Task;

        shutdownToken.Register(() =>
        {
            server.Shutdown();
        });

        return server;
    }

    public void Shutdown()
    {
        _appLifetime.StopApplication();
    }

    public void Dispose()
    {
        _appLifetime.StopApplication();
    }
}

public enum TestWebAppServerListenMode
{
    InsecureHttp1Only,
    InsecureHttp2Only,
    SecureHttp1Only,
    SecureHttp2Only,
    SecureHttp1AndHttp2,
}