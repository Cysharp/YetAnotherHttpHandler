using Microsoft.AspNetCore.Hosting;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace _YetAnotherHttpHandler.Test;

public class Http1Test(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    [Fact]
    public async Task FailedToConnect()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var ex = await Record.ExceptionAsync(async () => await httpClient.GetAsync($"http://localhost.exmample/"));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
        Assert.Contains("(Connect): dns error", ex.Message);
    }

    [Fact]
    public async Task FailedToConnect_VersionMismatch()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler() { Http2Only = true };
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var ex = await Record.ExceptionAsync(async () => (await httpClient.GetAsync($"{server.BaseUri}/")).EnsureSuccessStatusCode());

        // Assert
        Assert.IsType<HttpRequestException>(ex);
        Assert.Null(((HttpRequestException)ex).StatusCode);
        Assert.Contains("'HTTP_1_1_REQUIRED' (0xd)", ex.Message);
    }

    [Fact]
    public async Task Request_Version_20_Http1OnlyServer_Secure()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler()
        {
            // We need to verify server certificate.
            SkipCertificateVerification = false,
            RootCertificates = File.ReadAllText("./Certificates/localhost.crt")
        };
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.SecureHttp1Only, builder =>
        {
            // Use self-signed certificate for testing purpose.
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ConfigureHttpsDefaults(options =>
                {
                    options.ServerCertificate = new X509Certificate2("Certificates/localhost.pfx");
                });
            });
        });
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower, // Allow downgrade to HTTP/1.1. This is the default behavior on all .NET versions.
        };

        // Act
        var response = await httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }

    [Fact]
    public async Task Request_Version_Downgrade()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower, // Allow downgrade to HTTP/1.1. This is the default behavior on all .NET versions.
        };

        // Act
        var response = await httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }

    [Fact]
    public async Task Get_Ok()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);

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
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);

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
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/response-headers");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(new string[] { "foo" }, response.Headers.GetValues("x-test"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }

    [Fact]
    public async Task Get_NonAsciiPath()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/ハロー");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Konnichiwa", responseBody);
    }

    [Fact]
    public async Task Post_Cancel()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        var cts = new CancellationTokenSource();

        // Act
        var responseTask = httpClient.PostAsync($"{server.BaseUri}/slow-upload", content, cts.Token);
        await Task.Delay(1000);
        cts.Cancel();
        var ex = await Record.ExceptionAsync(async () => await responseTask);

        // Assert
        Assert.NotNull(ex);
        // NOTE: .NET's HttpClient will unwrap OperationCanceledException if an HttpRequestException containing OperationCanceledException is thrown.
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task Post_Timeout()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler) { Timeout = TimeSpan.FromSeconds(2) };
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        var cts = new CancellationTokenSource();

        // Act
        var responseTask = httpClient.PostAsync($"{server.BaseUri}/slow-upload", content);
        var ex = await Record.ExceptionAsync(async () => await responseTask);

        // Assert
        Assert.NotNull(ex);
        // NOTE: .NET's HttpClient will unwrap OperationCanceledException if an HttpRequestException containing OperationCanceledException is thrown.
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task Secure_Get_Ok()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler()
        {
            // We need to verify server certificate.
            SkipCertificateVerification = false,
            RootCertificates = File.ReadAllText("./Certificates/localhost.crt")
        };
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.SecureHttp1Only, builder =>
        {
            // Use self-signed certificate for testing purpose.
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ConfigureHttpsDefaults(options =>
                {
                    options.ServerCertificate = new X509Certificate2("Certificates/localhost.pfx");
                });
            });
        });

        // Act
        var response = await httpClient.GetAsync($"{server.BaseUri}/");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("__OK__", responseBody);
    }
}