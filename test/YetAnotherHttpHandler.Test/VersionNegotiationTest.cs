using System.Net;
using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class VersionNegotiationTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    [Theory]
    [MemberData(nameof(VersionNegotiationTestMatrix))]
    public async Task Negotiate(NegotiationCriteria criteria)
    {
        // Arrange
        var serverIsHttp11 = criteria.ServerListenVersions.HasFlag(ServerListenHttpVersion.Http1);
        var serverIsHttp20 = criteria.ServerListenVersions.HasFlag(ServerListenHttpVersion.Http2);
        var serverIsHttps = criteria.IsHttps;

        var listenMode =
            (serverIsHttp11 && serverIsHttp20)
                ? (serverIsHttps ? TestWebAppServerListenMode.SecureHttp1AndHttp2 : throw new InvalidOperationException("To listen to HTTP/1 and HTTP/2 on a single port, TLS is required."))
            : serverIsHttp11
                ? (serverIsHttps ? TestWebAppServerListenMode.SecureHttp1Only : TestWebAppServerListenMode.InsecureHttp1Only)
            : serverIsHttp20
                ? (serverIsHttps ? TestWebAppServerListenMode.SecureHttp2Only : TestWebAppServerListenMode.InsecureHttp2Only)
            : throw new InvalidOperationException();

        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(listenMode);
        using var httpHandler = new YetAnotherHttpHandler()
        {
            Http2Only = criteria.ClientHttp2Only,
            SkipCertificateVerification = true,
        };
        var httpClient = new HttpClient(httpHandler);

        // Act && Assert
        if (criteria.ExpectsException)
        {
            var response = default(HttpResponseMessage);
            var ex = await Record.ExceptionAsync(async () =>
            {
                response = await httpClient.GetAsync($"{server.BaseUri}/");
                response.EnsureSuccessStatusCode();
            });
            Assert.NotNull(ex);
            Assert.IsType<HttpRequestException>(ex);
        }
        else
        {
            var response = await httpClient.GetAsync($"{server.BaseUri}/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(criteria.ResponseVersion, response.Version);
        }
    }

    public static IEnumerable<object[]> VersionNegotiationTestMatrix() => VersionNegotiationTestMatrixCore().Select(x => new object[] { x });

    private static IEnumerable<NegotiationCriteria> VersionNegotiationTestMatrixCore()=>
    [
        // RequestVersion, ServerListenVersion, ClientHttp2Only, IsHttps, ExpectsException
        new(HttpVersion.Version11, ServerListenHttpVersion.Http1, HttpVersion.Version11, false, false, false), // HTTP/1.1 --(PlainText)                             --> HTTP/1.1 (OK)
        new(HttpVersion.Version11, ServerListenHttpVersion.Http1, HttpVersion.Version11, true, false, true),   // HTTP/1.1 --(PlainText, Force HTTP/2)               --> HTTP/1.1 (NG)
        new(HttpVersion.Version11, ServerListenHttpVersion.Http1, HttpVersion.Version11, false, true, false),  // HTTP/1.1 --(TLS)                                   --> HTTP/1.1 (OK)
        new(HttpVersion.Version11, ServerListenHttpVersion.Http1, HttpVersion.Version11, true, true, true),    // HTTP/1.1 --(TLS, Force HTTP/2)                     --> HTTP/1.1 (NG)

        new(HttpVersion.Version20, ServerListenHttpVersion.Http1, HttpVersion.Version11, false, false, false), // HTTP/2.0 --(PlainText, Downgrade HTTP/1.1)         --> HTTP/1.1 (OK)
        new(HttpVersion.Version20, ServerListenHttpVersion.Http1, HttpVersion.Version11, true, false, true),   // HTTP/2.0 --(PlainText, Force HTTP/2)               --> HTTP/1.1 (NG)
        new(HttpVersion.Version20, ServerListenHttpVersion.Http1, HttpVersion.Version11, false, true, false),  // HTTP/2.0 --(TLS, Downgrade HTTP/1.1)               --> HTTP/1.1 (OK)
        new(HttpVersion.Version20, ServerListenHttpVersion.Http1, HttpVersion.Version11, true, true, true),    // HTTP/2.0 --(TLS, Force HTTP/2)                     --> HTTP/1.1 (NG)

        new(HttpVersion.Version11, ServerListenHttpVersion.Http2, HttpVersion.Version20, false, false, true),  // HTTP/1.1 --(PlainText)                             --> HTTP/2   (NG)
        new(HttpVersion.Version11, ServerListenHttpVersion.Http2, HttpVersion.Version20, true, false, false),  // HTTP/1.1 --(PlainText, Force HTTP/2)               --> HTTP/2   (OK)
        new(HttpVersion.Version11, ServerListenHttpVersion.Http2, HttpVersion.Version20, false, true, false),  // HTTP/1.1 --(TLS, Auto upgrade HTTP/2)              --> HTTP/2   (OK)
        new(HttpVersion.Version11, ServerListenHttpVersion.Http2, HttpVersion.Version20, true, true, false),   // HTTP/1.1 --(TLS, Force HTTP/2)                     --> HTTP/2   (OK)

        new(HttpVersion.Version20, ServerListenHttpVersion.Http2, HttpVersion.Version20, false, false, true),  // HTTP/2.0 --(PlainText, Non-prior knowledge)        --> HTTP/2   (NG)
        new(HttpVersion.Version20, ServerListenHttpVersion.Http2, HttpVersion.Version20, true, false, false),  // HTTP/2.0 --(PlainText, Force HTTP/2)               --> HTTP/2   (OK)
        new(HttpVersion.Version20, ServerListenHttpVersion.Http2, HttpVersion.Version20, false, true, false),  // HTTP/2.0 --(TLS)                                   --> HTTP/2   (OK)
        new(HttpVersion.Version20, ServerListenHttpVersion.Http2, HttpVersion.Version20, true, true, false),   // HTTP/2.0 --(TLS, Force HTTP/2)                     --> HTTP/2   (OK)

        new(HttpVersion.Version11, ServerListenHttpVersion.Http1AndHttp2, HttpVersion.Version20, false, true, false),  // HTTP/1.1 --(TLS, Auto upgrade HTTP/2)      --> HTTP/1.1 & 2   (OK)
        new(HttpVersion.Version11, ServerListenHttpVersion.Http1AndHttp2, HttpVersion.Version20, true, true, false),   // HTTP/1.1 --(TLS, Force HTTP/2)             --> HTTP/1.1 & 2   (OK)

        new(HttpVersion.Version20, ServerListenHttpVersion.Http1AndHttp2, HttpVersion.Version20, false, true, false),  // HTTP/2.0 --(TLS)                           --> HTTP/1.1 & 2   (OK)
        new(HttpVersion.Version20, ServerListenHttpVersion.Http1AndHttp2, HttpVersion.Version20, true, true, false),   // HTTP/2.0 --(TLS, Force HTTP/2)             --> HTTP/1.1 & 2   (OK)
    ];

    [Flags]
    public enum ServerListenHttpVersion
    {
        Http1 = 1,
        Http2 = 2,
        Http1AndHttp2 = Http1 | Http2,
    }

    public record NegotiationCriteria(Version RequestVersion, ServerListenHttpVersion ServerListenVersions, Version ResponseVersion, bool ClientHttp2Only, bool IsHttps, bool ExpectsException);
}