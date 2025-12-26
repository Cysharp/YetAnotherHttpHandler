using System.Security.Cryptography.X509Certificates;
using Cysharp.Net.Http;
using HttpClientTestServer;

namespace _YetAnotherHttpHandler.Test;

public class ClientCertificateTest : UseTestServerTestBase
{
    public ClientCertificateTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    protected Task<ITestServer> LaunchServerAsync()
        => LaunchServerAsync(new TestServerOptions(ListenHttpProtocols.Http1AndHttp2, isSecure: true) { EnableClientCertificateValidation = true });

    [Fact]
    public async Task NotSet()
    {
        // Arrange
        await using var server = await LaunchServerAsync();
        using var httpHandler = new YetAnotherHttpHandler()
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
        //Assert.Contains("CertificateUnknown", ex.Message);
    }

    [Fact]
    public async Task UseClientCertificate()
    {
        // Arrange
        await using var server = await LaunchServerAsync();
        using var httpHandler = new YetAnotherHttpHandler()
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
        await using var server = await LaunchServerAsync();
        using var httpHandler = new YetAnotherHttpHandler()
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
        await using var server = await LaunchServerAsync();
        using var httpHandler = new SocketsHttpHandler();
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
        await using var server = await LaunchServerAsync();
        using var httpHandler = new SocketsHttpHandler();
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
        await using var server = await LaunchServerAsync();
        using var httpHandler = new SocketsHttpHandler();
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
