using System.Security.Cryptography.X509Certificates;
using _YetAnotherHttpHandler.Test.Helpers.Testing;
using Cysharp.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

[OSSkipCondition(OperatingSystems.Windows)]
public class UdsTest(ITestOutputHelper testOutputHelper) : Http2TestBase(testOutputHelper)
{
    private readonly string _udsPath = Path.Combine(Path.GetTempPath(), $"yaha-uds-test-{Guid.NewGuid()}");
    protected override YetAnotherHttpHandler CreateHandler()
    {
        return new YetAnotherHttpHandler()
        {
            UnixDomainSocketPath = _udsPath,
            Http2Only = true,
        };
    }

    protected override Task<TestWebAppServer> LaunchServerAsyncCore<T>(Action<WebApplicationBuilder>? configure = null)
    {
        return LaunchServerAsync<T>(TestWebAppServerListenMode.SecureHttp2Only, builder =>
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenUnixSocket(_udsPath, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
                
                // hyperlocal uses the 'unix' scheme and passes the URI to hyper. As a result, the ':scheme' header in the request is set to 'unix'.
                // By default, Kestrel does not accept non-HTTP schemes. To allow non-HTTP schemes, we need to set 'AllowAlternateSchemes' to true.
                options.AllowAlternateSchemes = true;  
            });

            configure?.Invoke(builder);
        });
    }
}

[OSSkipCondition(OperatingSystems.MacOSX | OperatingSystems.Linux)]
public class UdsNotSupportedTest
{
    [ConditionalFact]
    public async Task Throw()
    {
        // Arrange
        var handler = new YetAnotherHttpHandler()
        {
            UnixDomainSocketPath = "/path/to/socket",
        };
        var httpClient = new HttpClient(handler);

        // Act & Assert
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
        {
            await httpClient.GetAsync("http://localhost/");
        });
    }
}