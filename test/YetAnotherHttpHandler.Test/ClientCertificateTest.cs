using System.Security.Cryptography.X509Certificates;
using Cysharp.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class ClientCertificateTest : UseTestServerTestBase
{
    public ClientCertificateTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    protected Task<TestWebAppServer> LaunchServerAsync<T>(Action<WebApplicationBuilder>? configure = null)
        where T : ITestServerBuilder
    {
        return LaunchServerAsync<T>(TestWebAppServerListenMode.SecureHttp1AndHttp2, builder =>
        {
            // Use self-signed certificate for testing purpose.
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ConfigureHttpsDefaults(options =>
                {
                    options.ServerCertificate = new X509Certificate2("Certificates/localhost.pfx");
                    options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    options.ClientCertificateValidation = (certificate2, chain, policyError) =>
                    {
                        return certificate2.Subject == "CN=client.example.com";
                    };
                });
            });

            configure?.Invoke(builder);
        });
    }

    [Fact]
    public async Task NotSet()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        var httpHandler = new YetAnotherHttpHandler()
        {
            //ClientAuthCertificates = File.ReadAllText("./Certificates/client.crt"),
            //ClientAuthKey = File.ReadAllText("./Certificates/client.key"),
            RootCertificates = File.ReadAllText("./Certificates/localhost.crt")
        };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
        Assert.Contains("CertificateUnknown", ex.Message);
    }

    [Fact]
    public async Task UseClientCertificate()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        var httpHandler = new YetAnotherHttpHandler()
        {
            ClientAuthCertificates = File.ReadAllText("./Certificates/client.crt"),
            ClientAuthKey = File.ReadAllText("./Certificates/client.key"),
            RootCertificates = File.ReadAllText("./Certificates/localhost.crt")
        };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal("__OK__", result);
    }
    
    [Fact]
    public async Task Invalid()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        var httpHandler = new YetAnotherHttpHandler()
        {
            ClientAuthCertificates = File.ReadAllText("./Certificates/client_unknown.crt"), // CN=unknown.example.com
            ClientAuthKey = File.ReadAllText("./Certificates/client_unknown.key"),
            RootCertificates = File.ReadAllText("./Certificates/localhost.crt")
        };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
    }

    [Fact]
    public async Task Reference_SocketHttpHandler_NotSet()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        var httpHandler = new SocketsHttpHandler();
        httpHandler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
    }

    [Fact]
    public async Task Reference_SocketHttpHandler_UseClientCertificate()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        var httpHandler = new SocketsHttpHandler();
        httpHandler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
        httpHandler.SslOptions.ClientCertificates = new X509CertificateCollection()
        {
            X509Certificate.CreateFromCertFile("./Certificates/client.pfx")
        };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal("__OK__", result);
    }

    [Fact]
    public async Task Reference_SocketHttpHandler_Invalid()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        var httpHandler = new SocketsHttpHandler();
        httpHandler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
        httpHandler.SslOptions.ClientCertificates = new X509CertificateCollection()
        {
            X509Certificate.CreateFromCertFile("./Certificates/client_unknown.pfx")
        };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
    }
}