using System.Security.Cryptography.X509Certificates;
using Cysharp.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class Http2Test(ITestOutputHelper testOutputHelper) : Http2TestBase(testOutputHelper)
{
    protected override YetAnotherHttpHandler CreateHandler()
    {
        // Use self-signed certificate for testing purpose.
        return new YetAnotherHttpHandler()
        {
            SkipCertificateVerification = true,
            //Http2MaxFrameSize = 1024 * 1024,
        };
    }

    protected override Task<TestWebAppServer> LaunchServerAsyncCore<T>(Action<WebApplicationBuilder>? configure = null)
    {
        return LaunchServerAsync<T>(TestWebAppServerListenMode.SecureHttp2Only, builder =>
        {
            // Use self-signed certificate for testing purpose.
            builder.WebHost.ConfigureKestrel(options =>
            {
                //options.Limits.Http2.MaxFrameSize = 1024 * 1024;
                options.ConfigureHttpsDefaults(options =>
                {
                    options.ServerCertificate = new X509Certificate2("Certificates/localhost.pfx");
                });
            });

            configure?.Invoke(builder);
        });
    }

    [Fact]
    public async Task SelfSignedCertificate_NotTrusted()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        using var httpHandler = new YetAnotherHttpHandler() { SkipCertificateVerification = false }; // We need to verify server certificate.
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
    }

    [Fact]
    public async Task SelfSignedCertificate_NotTrusted_SkipValidation()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        using var httpHandler = new YetAnotherHttpHandler() { SkipCertificateVerification = true };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal("__OK__", result);
    }

    [Fact]
    public async Task SelfSignedCertificate_Trusted_CustomRootCA()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        using var httpHandler = new YetAnotherHttpHandler()
        {
            // We need to verify server certificate.
            SkipCertificateVerification = false,
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
    public async Task CustomCertificateVerificationHandler_Success()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        using var httpHandler = new YetAnotherHttpHandler()
        {
            OnVerifyServerCertificate = (name, certificate, now) =>
            {
                // Accept any certificate.
                return true;
            }
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
    public async Task CustomCertificateVerificationHandler_Failure()
    {
        // Arrange
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        using var httpHandler = new YetAnotherHttpHandler()
        {
            OnVerifyServerCertificate = (name, certificate, now) =>
            {
                // Reject any certificate.
                return false;
            }
        };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
    }

    [Fact]
    public async Task CustomCertificateVerificationHandler_Certificate()
    {
        // Arrange
        byte[] receivedCertificate = Array.Empty<byte>();
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        using var httpHandler = new YetAnotherHttpHandler()
        {
            OnVerifyServerCertificate = (name, certificate, now) =>
            {
                receivedCertificate = certificate.ToArray();
                return true;
            }
        };
        var httpClient = new HttpClient(httpHandler);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/");
        var response = await httpClient.SendAsync(request);
        await response.Content.ReadAsStringAsync();

        // Assert
        var cert = new X509Certificate2(receivedCertificate);
        Assert.Equal("CN=localhost", cert.Issuer);
        Assert.Equal("510DF72DB50FC2580A5E6CC0EFB2A60DB576B989", cert.SerialNumber);
        Assert.Equal("143a97beaaf96af9cdbc8769f523251b7c60625b", cert.Thumbprint, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("3082020a0282020100caec8d9f7e92bfceeef2aed4981ee786e31eef5c3a81e52cb5c60a84ee596a161f77d23d9866532a952f4e1826eb99eb20f4fccc4ef246aae06a03fe32db91b70072b7dc3b94ef18fe525d6b88876935dd7bd62ad25646aefd02799ecd2590867cf1bd1891c6f018782238e133019c35e6b9d2f7e4cbbda7b999c274b28b6ebe79d37edb210ed1d9b3dfc6a353a342dc8a3e40a48ebf625a49675e2f8b4c870d20b24c033d3d4960bfa12c61d437bdfd426dd8365659cf880c982367fb4d1677601aeb514f3fa9c052b0fa1335880268195ddf9ff7262ccb649956ba43d69db7b74ee53d17046ffd587e5270f6727bfba74f895b67286bff58ae4fd6ee8485c46a2e8e0fdf2a6ecef63efa24a0ffd3853bc86922cd01b8b9a928dc14dc20380746c0eac88a7d311daffe76dbf17a25d9c1cbf67137fa8beb827f6ea4816f81993e9c0b91800981e08681b4c4b98a1d1026794b07916a0e3f8f9091beb3c8aa3a9860e15dcfd3bcc0bce4e4d49f7a5432ce967407a63c0c197a1869ebd61d153e5669b6ae4b694f2255b79c6d96a8e837f1419c261b429028b12e5f72237b82172ccc298a54a0b4ae209aeccc0f528a1539afd3a83cefe6b18fa76bc55d374a0e8bdaece953a366287c377682b3b03393bb4d0a7b30752cd60b671d211874c78a5a2e9f49bb71e09aeed776e55d5ecc3141da402836629248aa87e75d0e7ef4530203010001", cert.GetPublicKeyString(), StringComparer.OrdinalIgnoreCase);
    }
}