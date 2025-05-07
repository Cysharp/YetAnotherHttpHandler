using Microsoft.AspNetCore.Builder;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using Cysharp.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using TestWebApp;

namespace _YetAnotherHttpHandler.Test;

public abstract class Http2TestBase(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    protected abstract YetAnotherHttpHandler CreateHandler();
    protected abstract Task<TestWebAppServer> LaunchServerAsyncCore<T>(Action<WebApplicationBuilder>? configure = null) where T : ITestServerBuilder;

    protected Task<TestWebAppServer> LaunchServerAsync<T>(Action<WebApplicationBuilder>? configure = null) where T : ITestServerBuilder
        => LaunchServerAsyncCore<T>(configure);

    [Fact]
    public async Task Get_Ok()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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
    public async Task Post_NotDuplex_Receive_ResponseHeaders_Before_ResponseBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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
    //[Fact]
    //public async Task Post_NotDuplex_DoNot_Receive_ResponseHeaders_Before_RequestBodyCompleted()
    //{
    //    // Arrange
    //    using var httpHandler = CreateHandler();
    //    var httpClient = new HttpClient(httpHandler);
    //    await using var server = await LaunchAsync<TestServerForHttp1AndHttp2>();
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

    [Fact]
    public async Task Post_NotDuplex_Body_StreamingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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

    [Fact]
    public async Task Post_Duplex_Body_StreamingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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

    [Fact]
    public async Task Post_ResponseTrailers()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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

    [Fact]
    public async Task AbortOnServer_Post_SendingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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

    [Fact]
    public async Task Cancel_Post_SendingBody()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        //using var httpHandler = new SocketsHttpHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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
        // NOTE: .NET's HttpClient will unwrap OperationCanceledException if an HttpRequestException containing OperationCanceledException is thrown.
        var operationCanceledException = Assert.IsAssignableFrom<OperationCanceledException>(ex);
#if !UNITY_2021_1_OR_NEWER
        // NOTE: Unity's Mono HttpClient internally creates a new CancellationTokenSource.
        Assert.Equal(cts.Token, operationCanceledException.CancellationToken);
#endif
    }

#if !UNITY_2021_1_OR_NEWER
    [Fact]
    public async Task Cancel_Post_SendingBody_Duplex()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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
#endif

    [Fact]
    public async Task DisposeHttpResponseMessage_Post_SendingBody_Duplex()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

        // Act
        var pipe = new Pipe();
        var content = new DuplexStreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-null-duplex")
        {
            Version = HttpVersion.Version20,
            Content = content,
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken);

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        cts.Token.Register(() => response.Dispose());
        var ex = await Record.ExceptionAsync(async () => await response.Content.ReadAsByteArrayAsync().WaitAsync(TimeoutToken));
        //TestOutputHelper.WriteLine(ex?.ToString());

        // Assert
        Assert.NotNull(ex);
        Assert.IsAssignableFrom<HttpRequestException>(ex);
        Assert.IsAssignableFrom<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task Cancel_Get_BeforeReceivingResponseHeaders()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        var id = Guid.NewGuid().ToString();

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUri}/slow-response-headers")
        {
            Version = HttpVersion.Version20,
            Headers = {  { TestServerForHttp1AndHttp2.SessionStateHeaderKey, id } }
        };
        
        // The server responds after one second. But the client cancels the request before receiving response headers.
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var ex = await Record.ExceptionAsync(async () => await httpClient.SendAsync(request, cts.Token).WaitAsync(TimeoutToken));
        await Task.Delay(100);
        var isCanceled = await httpClient.GetStringAsync($"{server.BaseUri}/session-state?id={id}&key=IsCanceled").WaitAsync(TimeoutToken);

        // Assert
        var operationCanceledException = Assert.IsAssignableFrom<OperationCanceledException>(ex);
#if !UNITY_2021_1_OR_NEWER
        // NOTE: Unity's Mono HttpClient internally creates a new CancellationTokenSource.
        Assert.Equal(cts.Token, operationCanceledException.CancellationToken);
#endif
        Assert.Equal("True", isCanceled);
    }

    [Fact]
    public async Task Cancel_Post_BeforeRequest()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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
#if !UNITY_2021_1_OR_NEWER
        // NOTE: Unity's Mono HttpClient internally creates a new CancellationTokenSource.
        Assert.Equal(ct, operationCanceledException.CancellationToken);
#endif
    }

    [Fact]
    public async Task Grpc_Unary()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        var client = new Greeter.GreeterClient(GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler }));

        // Act
        var response = await client.SayHelloAsync(new HelloRequest { Name = "Alice" }, deadline: DateTime.UtcNow.AddSeconds(5));

        // Assert
        Assert.Equal("Hello Alice", response.Message);
    }

    [Fact]
    public async Task Grpc_Duplex()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        var client = new Greeter.GreeterClient(GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler }));

        // Act
        var request = client.SayHelloDuplex(deadline:  DateTime.UtcNow.AddSeconds(10));
        var responses = new List<string>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var response in request.ResponseStream.ReadAllAsync())
            {
                responses.Add(response.Message);
            }
        });
        for (var i = 0; i < 5; i++)
        {
            await request.RequestStream.WriteAsync(new HelloRequest { Name = $"User-{i}" }, TimeoutToken);
            await Task.Delay(500);
        }
        // all requests are processed on the server and receive the responses. (but the request stream is not completed at this time)
        var responsesBeforeCompleted = responses.ToArray();

        // complete the request stream.
        await request.RequestStream.CompleteAsync().WaitAsync(TimeoutToken);
        await readTask.WaitAsync(TimeoutToken);

        // Assert
        Assert.Equal(new [] { "Hello User-0", "Hello User-1", "Hello User-2", "Hello User-3", "Hello User-4" }, responsesBeforeCompleted);
        Assert.Equal(new [] { "Hello User-0", "Hello User-1", "Hello User-2", "Hello User-3", "Hello User-4" }, responses);
    }


    [Fact]
    public async Task Grpc_Duplex_Concurrency()
    {
        // Arrange
        const int RequestCount = 10;
        const int Concurrency = 10;
        using var httpHandler = CreateHandler();
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        using var channel = GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler });

        // Act
        var tasks = new List<Task<(IReadOnlyList<string> ResponsesBeforeCompleted, IReadOnlyList<string> Responses)>>();
        for (var i = 0; i < Concurrency; i++)
        {
            tasks.Add(DoRequestAsync(i * 1000, channel, TimeoutToken));
        }
        var results = await Task.WhenAll(tasks);

        static async Task<(IReadOnlyList<string> ResponsesBeforeCompleted, IReadOnlyList<string> Responses)> DoRequestAsync(int sequenceBase, ChannelBase channel, CancellationToken cancellationToken)
        {
            var client = new Greeter.GreeterClient(channel);
            var request = client.SayHelloDuplex(deadline: DateTime.UtcNow.AddSeconds(10));
            var responses = new List<string>();
            var readTask = Task.Run(async () =>
            {
                await foreach (var response in request.ResponseStream.ReadAllAsync())
                {
                    responses.Add(response.Message);
                }
            });
            for (var i = 0; i < RequestCount; i++)
            {
                await request.RequestStream.WriteAsync(new HelloRequest { Name = $"User-{i + sequenceBase}" }, cancellationToken);
                await Task.Delay(500);
            }
            // all requests are processed on the server and receive the responses. (but the request stream is not completed at this time)
            var responsesBeforeCompleted = responses.ToArray();

            // complete the request stream.
            await request.RequestStream.CompleteAsync().WaitAsync(cancellationToken);
            await readTask.WaitAsync(cancellationToken);

            return (responsesBeforeCompleted, responses);
        }

        // Assert
        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(Enumerable.Range((i * 1000), RequestCount).Select(x => $"Hello User-{x}"), results[i].ResponsesBeforeCompleted);
            Assert.Equal(Enumerable.Range((i * 1000), RequestCount).Select(x => $"Hello User-{x}"), results[i].Responses);
        }
    }

    [Fact]
    public async Task Grpc_ShutdownAndDispose()
    {
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

        for (var i = 0; i < 10; i++)
        {
            await RunAsync();
            GC.GetTotalMemory(forceFullCollection: true);
        }


        async Task RunAsync()
        {
            // Arrange
            var httpHandler = CreateHandler();
            var channel = GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions()
            {
                HttpHandler = httpHandler,
                DisposeHttpClient = true,
            });
            var client = new Greeter.GreeterClient(channel);

            // Act
            var duplexStreaming = client.SayHelloDuplex();
            await duplexStreaming.RequestStream.WriteAsync(new HelloRequest()).WaitAsync(TimeoutToken);
            await duplexStreaming.ResponseHeadersAsync.WaitAsync(TimeoutToken);

            duplexStreaming.Dispose();
            duplexStreaming = null;
            client = null;
            GC.GetTotalMemory(forceFullCollection: true);

            await channel.ShutdownAsync().WaitAsync(TimeoutToken);
            GC.GetTotalMemory(forceFullCollection: true);

            channel.Dispose();
            channel = null;
            GC.GetTotalMemory(forceFullCollection: true);

            httpHandler.Dispose();
            httpHandler = null;
            GC.GetTotalMemory(forceFullCollection: true);
        }
    }

    [Fact]
    public async Task Grpc_Error_Status_ErrorCode()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        var client = new Greeter.GreeterClient(GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler }));

        // Act
        var ex = await Record.ExceptionAsync(async () => await client.ResetByServerAsync(new ResetRequest { ErrorCode = 0x8 /* CANCELED */ }, deadline: DateTime.UtcNow.AddSeconds(5)));

        // Assert
        Assert.IsType<RpcException>(ex);
        Assert.Equal(StatusCode.Cancelled, ((RpcException)ex).StatusCode);
    }

    [Fact]
    public async Task Grpc_Error_Status_Unavailable_By_IOException()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        var client = new Greeter.GreeterClient(GrpcChannel.ForAddress("http://server.does.not.exists", new GrpcChannelOptions() { HttpHandler = httpHandler }));

        // Act
        var ex = await Record.ExceptionAsync(async () => await client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, deadline: DateTime.UtcNow.AddSeconds(5)));

        // Assert
        Assert.IsType<RpcException>(ex);
        Assert.Equal(StatusCode.Unavailable, ((RpcException)ex).StatusCode);
    }

    [Fact]
    public async Task Grpc_Error_TimedOut_With_HttpClientTimeout()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler) { Timeout = TimeSpan.FromSeconds(3) };
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        var client = new Greeter.GreeterClient(GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpClient = httpClient }));

        // Act
        var ex = await Record.ExceptionAsync(async () => await client.SayHelloNeverAsync(new HelloRequest() { Name = "Alice" }));

        // Assert
        Assert.IsType<RpcException>(ex);
        Assert.Equal(StatusCode.Cancelled, ((RpcException)ex).StatusCode);
#if UNITY_2021_1_OR_NEWER
        Assert.IsType<OperationCanceledException>(((RpcException)ex).Status.DebugException);
#else
        Assert.IsType<TaskCanceledException>(((RpcException)ex).Status.DebugException);
#endif
    }

    [Fact]
    public async Task Grpc_Error_TimedOut_With_Deadline()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        var client = new Greeter.GreeterClient(GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpClient = httpClient }));

        // Act
        var ex = await Record.ExceptionAsync(async () => await client.SayHelloNeverAsync(new HelloRequest() { Name = "Alice" }, deadline: DateTime.UtcNow.AddSeconds(3)));

        // Assert
        Assert.IsType<RpcException>(ex);
        Assert.Equal(StatusCode.DeadlineExceeded, ((RpcException)ex).StatusCode);
    }

    [Fact]
    public async Task Grpc_Error_TimedOut_With_CancellationToken()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();
        var client = new Greeter.GreeterClient(GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpClient = httpClient }));

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var ex = await Record.ExceptionAsync(async () => await client.SayHelloNeverAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: cts.Token));

        // Assert
        Assert.IsType<RpcException>(ex);
        Assert.Equal(StatusCode.Cancelled, ((RpcException)ex).StatusCode);
    }

    [Fact]
    public async Task Enable_Http2KeepAlive()
    {
        // Arrange
        using var httpHandler = CreateHandler();
        httpHandler.Http2KeepAliveInterval = TimeSpan.FromSeconds(5);
        httpHandler.Http2KeepAliveTimeout = TimeSpan.FromSeconds(5);
        httpHandler.Http2KeepAliveWhileIdle = true;

        var httpClient = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>();

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