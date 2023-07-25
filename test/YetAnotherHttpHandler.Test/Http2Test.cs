using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Cysharp.Net.Http;
using Microsoft.AspNetCore.Hosting;
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

    [ConditionalFact]
    public async Task SelfSignedCertificate_NotTrusted()
    {
        // Arrange
        await using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp1AndHttp2, builder =>
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ConfigureHttpsDefaults(options =>
                {
                    options.ServerCertificate = new X509Certificate2("Certificates/localhost.pfx");
                });
            });
        });
        var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request));

        // Arrange
        Assert.IsType<HttpRequestException>(ex);
    }
    
    [ConditionalFact]
    public async Task SelfSignedCertificate_NotTrusted_SkipValidation()
    {
        // Arrange
        await using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp1AndHttp2, builder =>
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ConfigureHttpsDefaults(options =>
                {
                    options.ServerCertificate = new X509Certificate2("Certificates/localhost.pfx");
                });
            });
        });
        var httpHandler = new YetAnotherHttpHandler() { SkipCertificateVerification = true };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        // Arrange
        Assert.Equal("__OK__", result);
    }
}