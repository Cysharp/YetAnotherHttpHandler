using Microsoft.AspNetCore.Builder;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public abstract class Http2TestBase : UseTestServerTestBase
{
    protected Http2TestBase(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    protected abstract HttpMessageHandler CreateHandler();
    protected abstract Task<TestWebAppServer> LaunchServerAsyncCore<T>(Action<WebApplicationBuilder>? configure = null) where T : ITestServerBuilder;

    protected Task<TestWebAppServer> LaunchServerAsync<T>(Action<WebApplicationBuilder>? configure = null) where T : ITestServerBuilder
        => LaunchServerAsyncCore<T>(configure);

    [ConditionalFact]
    public async Task Get_Ok()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

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

    [ConditionalFact]
    public async Task Get_NotOk()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

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

    [ConditionalFact]
    public async Task Post_Body()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

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

    [ConditionalFact]
    public async Task Post_NotDuplex_Receive_ResponseHeaders_Before_ResponseBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

        // Act
        var content = new ByteArrayContent(new byte[] { 0 });
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
    
    // NOTE: SocketHttpHandler waits for the completion of sending the request body before the response headers.
    //       https://github.com/dotnet/runtime/blob/v7.0.0/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/Http2Connection.cs#L1980-L1988
    //       https://github.com/dotnet/runtime/blob/v7.0.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpContent.cs#L343-L349
    //[ConditionalFact]
    //public async Task Post_NotDuplex_DoNot_Receive_ResponseHeaders_Before_RequestBodyCompleted()
    //{
    //    // Arrange
    //    using var httpHandler = CreateHandler();
    //    var httpClient = new HttpClient(httpHandler);
    //    await using var server = await LaunchAsync<TestServerForHttp2>();
    //
    //    // Act
    //    var pipe = new Pipe();
    //    var content = new StreamContent(pipe.Reader.AsStream());
    //    content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
    //    var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-response-headers-immediately")
    //    {
    //        Version = HttpVersion.Version20,
    //        Content = content,
    //    };
    //    var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
    //    var ex = await Assert.ThrowsAsync<TaskCanceledException>(async () => await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(timeout.Token));
    //
    //    // Assert
    //    Assert.Equal(timeout.Token, ex.CancellationToken);
    //}

    [ConditionalFact]
    public async Task Post_NotDuplex_Body_StreamingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

        // Act
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-streaming")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
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
        var response = await httpClient.SendAsync(request).WaitAsync(TimeoutToken);
        var isSendCompletedAfterSendAsync = taskSend.IsCompleted;
        var responseBody = await response.Content.ReadAsStringAsync().WaitAsync(TimeoutToken); // = request body bytes.

        // Assert
        Assert.True(isSendCompletedAfterSendAsync);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1024 * 1024 * 10).ToString(), responseBody);
    }

    [ConditionalFact]
    public async Task Post_Duplex_Body_StreamingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

        // Act
        var pipe = new Pipe();
        var content = new DuplexStreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-streaming")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
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
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken); // wait for receive response headers.
        var isSendCompletedAfterSendAsync = taskSend.IsCompleted; // Sending request body is not completed yet.
        var responseBody = await response.Content.ReadAsStringAsync().WaitAsync(TimeoutToken); // = request body bytes.
        await taskSend;

        // Assert
        Assert.False(isSendCompletedAfterSendAsync);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1024 * 1024 * 10).ToString(), responseBody);
    }

    [ConditionalFact]
    public async Task Post_ResponseTrailers()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

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

    [ConditionalFact]
    public async Task AbortOnServer_Post_SendingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

        // Act
        var content = new ByteArrayContent(Enumerable.Range(0, 1024 * 1024).Select(x => (byte)(x % 255)).ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-abort-while-reading")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request).WaitAsync(TimeoutToken));

        // Assert
        Assert.IsType<HttpRequestException>(ex);
    }

    [ConditionalFact]
    public async Task Cancel_Post_SendingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        //using var httpHandler = new SocketsHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

        // Act
        var pipe = new Pipe();
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-null")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request, cts.Token).WaitAsync(TimeoutToken));

        // Assert
        var operationCanceledException = Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(cts.Token, operationCanceledException.CancellationToken);
    }

    [ConditionalFact]
    public async Task Cancel_Post_SendingBody_Duplex()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

        // Act
        var pipe = new Pipe();
        var content = new DuplexStreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-null-duplex")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var ex = await Record.ExceptionAsync(async () => await response.Content.ReadAsByteArrayAsync(cts.Token).WaitAsync(TimeoutToken));

        // Assert
        var operationCanceledException = Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(cts.Token, operationCanceledException.CancellationToken);
    }

    [ConditionalFact]
    public async Task Cancel_Post_BeforeRequest()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp2>();

        // Act
        var pipe = new Pipe();
        var content = new DuplexStreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-null")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        var ct = new CancellationToken(true);
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request, ct).WaitAsync(TimeoutToken));

        // Assert
        var operationCanceledException = Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(ct, operationCanceledException.CancellationToken);
    }

    // Content with default value of true for AllowDuplex because AllowDuplex is internal.
    class DuplexStreamContent : HttpContent
    {
        private readonly Stream _stream;

        public DuplexStreamContent(Stream stream)
        {
            _stream = stream;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => _stream.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}