using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using _YetAnotherHttpHandler.Test.Helpers;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public class Http2Test : UseTestServerTestBase
{
    public Http2Test(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task Get_Ok()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp2Only);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/")
        {
            Version = HttpVersion.Version20,
        };
        var response = await httpClient.SendAsync(request).WaitAsync(TimeoutToken);
        var responseBody = await response.Content.ReadAsStringAsync().WaitAsync(TimeoutToken);

        // Assert
        Assert.Equal("__OK__", responseBody);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_NotOk()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp2Only);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/not-found")
        {
            Version = HttpVersion.Version20,
        };
        var response = await httpClient.SendAsync(request).WaitAsync(TimeoutToken);
        var responseBody = await response.Content.ReadAsStringAsync().WaitAsync(TimeoutToken);

        // Assert
        Assert.Equal("__Not_Found__", responseBody);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Body()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp2Only);

        // Act
        var content = new ByteArrayContent(new byte[] { 1, 2, 3, 45, 67 });
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-echo")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var response = await httpClient.SendAsync(request).WaitAsync(TimeoutToken);
        var responseBody = await response.Content.ReadAsByteArrayAsync().WaitAsync(TimeoutToken);

        // Assert
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3, 45, 67 }, responseBody);
        Assert.Equal("application/octet-stream", response.Headers.TryGetValues("x-request-content-type", out var values) ? string.Join(',', values) : null);
    }

    [Fact]
    public async Task Post_Receive_ResponseHeaders_Before_RequestBody()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp2Only);

        // Act
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-response-headers-immediately")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken); // wait for receive response headers.
        var responseBodyTask = response.Content.ReadAsByteArrayAsync().WaitAsync(TimeoutToken);
        await Task.Delay(100);

        // Assert
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("foo", response.Headers.TryGetValues("x-header-1", out var values) ? string.Join(',', values) : null);
        Assert.False(responseBodyTask.IsCompleted);
    }

    [Fact]
    public async Task Post_Body_StreamingBody()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp2Only);

        // Act
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-streaming")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken); // wait for receive response headers.
        var written = 0L;
        var taskSend = Task.Run(async () =>
        {
            // 10 MB
            var dataChunk = Enumerable.Range(0, 1024 * 1024).Select(x => (byte)(x % 255)).ToArray();
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(100);
                await pipe.Writer.WriteAsync(dataChunk);
                written += dataChunk.Length;
            }
            await pipe.Writer.CompleteAsync();
        });
        await taskSend.WaitAsync(TimeoutToken);
        var responseBody = await response.Content.ReadAsStringAsync().WaitAsync(TimeoutToken); // = request body bytes.

        // Assert
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1024 * 1024 * 10).ToString(), responseBody);
    }

    [Fact]
    public async Task Post_ResponseTrailers()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        using var server = await LaunchAsync<TestServerForHttp2>(TestWebAppServerListenMode.SecureHttp2Only);

        // Act
        var content = new ByteArrayContent(new byte[] { 1, 2, 3, 45, 67 });
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-response-trailers")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var response = await httpClient.SendAsync(request).WaitAsync(TimeoutToken);
        var responseBody = await response.Content.ReadAsByteArrayAsync().WaitAsync(TimeoutToken);

        // Assert
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("foo", response.TrailingHeaders.TryGetValues("x-trailer-1", out var values) ? string.Join(',', values) : null);
        Assert.Equal("bar", response.TrailingHeaders.TryGetValues("x-trailer-2", out var values2) ? string.Join(',', values2) : null);
    }

}