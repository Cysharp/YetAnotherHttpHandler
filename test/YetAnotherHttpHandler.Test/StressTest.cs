#define ENABLE_STRESS_TEST

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Cysharp.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using TestWebApp;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

[OSSkipCondition(OperatingSystems.MacOSX)]
public class StressTest : UseTestServerTestBase
{
    protected override TimeSpan UnexpectedTimeout => TimeSpan.FromMinutes(3);

    public StressTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    protected HttpMessageHandler CreateHandler()
    {
        // Use self-signed certificate for testing purpose.
        return new YetAnotherHttpHandler()
        {
            SkipCertificateVerification = true,
            //Http2MaxFrameSize = 1024 * 1024,
        };
    }

    protected Task<TestWebAppServer> LaunchServerAsyncCore<T>(Action<WebApplicationBuilder>? configure = null)
        where T : ITestServerBuilder
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

    protected Task<TestWebAppServer> LaunchServerAsync<T>(Action<WebApplicationBuilder>? configure = null) where T : ITestServerBuilder
        => LaunchServerAsyncCore<T>(configure);

    [ConditionalFact]
    public async Task Grpc_Duplex_Concurrency()
    {
        // Arrange
        const int RequestCount = 1000;
        const int Concurrency = 10;

        var requestStringSuffix = string.Create(1024 * 16, default(object), (span, _) => { span.Fill('X'); }); // UTF-8 = 64KiB
        using var httpHandler = CreateHandler();
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        using var channel = GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler });

        // Act
        var tasks = new List<Task<(IReadOnlyList<string> ResponsesBeforeCompleted, IReadOnlyList<string> Responses)>>();
        for (var i = 0; i < Concurrency; i++)
        {
            tasks.Add(DoRequestAsync(i * 1000, requestStringSuffix, channel, /*TimeoutToken*/ new CancellationTokenSource(TimeSpan.FromMinutes(10)).Token));
        }
        var results = await Task.WhenAll(tasks);

        static async Task<(IReadOnlyList<string> ResponsesBeforeCompleted, IReadOnlyList<string> Responses)> DoRequestAsync(int sequenceBase, string requestStringSuffix, ChannelBase channel, CancellationToken cancellationToken)
        {
            var client = new Greeter.GreeterClient(channel);
            var request = client.SayHelloDuplex(deadline: DateTime.UtcNow.AddMinutes(10));
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
                await request.RequestStream.WriteAsync(new HelloRequest { Name = $"User-{i + sequenceBase}-{requestStringSuffix}" }, cancellationToken);
                await Task.Delay(16);
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
            Assert.Equal(Enumerable.Range((i * 1000), RequestCount).Select(x => $"Hello User-{x}-{requestStringSuffix}"), results[i].ResponsesBeforeCompleted);
            Assert.Equal(Enumerable.Range((i * 1000), RequestCount).Select(x => $"Hello User-{x}-{requestStringSuffix}"), results[i].Responses);
        }
    }

    [ConditionalFact]
    public async Task Grpc_Duplex_Concurrency_CompleteFromServerWhileSendingData()
    {
        // Arrange
        const int RequestCount = 1000;
        const int Concurrency = 2;
        const int MaxRetry = 5;

        var data = string.Create(1024 * 64, default(object), (span, _) => { span.Fill('X'); }); // UTF-8 = 64KB
        using var httpHandler = CreateHandler();
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        using var channel = GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler });

        // Act
        var tasks = new List<Task>();
        for (var i = 0; i < Concurrency; i++)
        {
            tasks.Add(DoRequestAsync(i * 1000, data, channel, /*TimeoutToken*/ new CancellationTokenSource(TimeSpan.FromMinutes(10)).Token));
        }
        await Task.WhenAll(tasks);

        static async Task DoRequestAsync(int sequenceBase, string data, ChannelBase channel, CancellationToken cancellationToken)
        {
            var maxRetry = MaxRetry;

        Retry:
            var client = new Greeter.GreeterClient(channel);
            var request = client.SayHelloDuplexCompleteRandomly(deadline: DateTime.UtcNow.AddMinutes(10));
            var responses = new List<string>();
            var readTask = Task.Run(async () =>
            {
                await foreach (var response in request.ResponseStream.ReadAllAsync())
                {
                    responses.Add(response.Message);
                }
            });
            try
            {
                for (var i = 0; i < RequestCount; i++)
                {
                    await request.RequestStream.WriteAsync(new HelloRequest { Name = data }, cancellationToken);
                    await Task.Delay(16);
                }
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.OK)
            {
                // Response has completed by the server.
            }

            // complete the request stream.
            await request.RequestStream.CompleteAsync().WaitAsync(cancellationToken);
            await readTask.WaitAsync(cancellationToken);

            // Retry if the request complete successfully.
            if (--maxRetry > 0)
            {
                goto Retry;
            }
        }
    }

    [ConditionalFact]
    public async Task Grpc_Duplex_Concurrency_AbortFromServerWhileSendingData()
    {
        // Arrange
        const int RequestCount = 1000;
        const int Concurrency = 2;
        const int MaxRetry = 5;

        var data = string.Create(1024 * 64, default(object), (span, _) => { span.Fill('X'); }); // UTF-8 = 64KB
        using var httpHandler = CreateHandler();
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        using var channel = GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler });

        // Act
        var tasks = new List<Task>();
        for (var i = 0; i < Concurrency; i++)
        {
            tasks.Add(DoRequestAsync(i * 1000, data, channel, /*TimeoutToken*/ new CancellationTokenSource(TimeSpan.FromMinutes(10)).Token));
        }
        await Task.WhenAll(tasks);

        static async Task DoRequestAsync(int sequenceBase, string data, ChannelBase channel, CancellationToken cancellationToken)
        {
            var maxRetry = MaxRetry;

        Retry:
            var client = new Greeter.GreeterClient(channel);
            var request = client.SayHelloDuplexAbortRandomly(deadline: DateTime.UtcNow.AddMinutes(10));
            var responses = new List<string>();
            var readTask = Task.Run(async () =>
            {
                await foreach (var response in request.ResponseStream.ReadAllAsync())
                {
                    responses.Add(response.Message);
                }
            });
            try
            {
                for (var i = 0; i < RequestCount; i++)
                {
                    await request.RequestStream.WriteAsync(new HelloRequest { Name = data }, cancellationToken);
                    await Task.Delay(16);
                }
            }
            catch (RpcException e) when (e.StatusCode is StatusCode.Unavailable or StatusCode.Internal)
            {
                // The request has aborted by the server.
            }

            // complete the request stream.
            await request.RequestStream.CompleteAsync().WaitAsync(cancellationToken);
            try
            {
                await readTask.WaitAsync(cancellationToken);
            }
            catch (RpcException e) when (e.StatusCode is StatusCode.Unavailable or StatusCode.Internal)
            {
                // The request has aborted by the server.
            }

            // Retry if the request complete successfully.
            if (--maxRetry > 0)
            {
                goto Retry;
            }
        }
    }

    [ConditionalFact]
    public async Task Grpc_Duplex_Large_Data()
    {
        // Arrange
        const int RequestCount = 1000;
        const int Concurrency = 3;
        var data = string.Create(1024 * 1024 /* UTF-8 = 1MB */, default(object), static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = (char)Random.Shared.Next(33, 126);
            }
        });
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(data));

        using var httpHandler = CreateHandler();
        await using var server = await LaunchServerAsync<TestServerForHttp2>();
        using var channel = GrpcChannel.ForAddress(server.BaseUri, new GrpcChannelOptions() { HttpHandler = httpHandler });

        // Act
        var tasks = new List<Task<IReadOnlyList<string>>>();
        for (var i = 0; i < Concurrency; i++)
        {
            tasks.Add(DoRequestAsync(i * 1000, data, channel, /*TimeoutToken*/ new CancellationTokenSource(TimeSpan.FromMinutes(10)).Token));
        }
        var results = await Task.WhenAll(tasks);

        static async Task<IReadOnlyList<string>> DoRequestAsync(int sequenceBase, string data, ChannelBase channel, CancellationToken cancellationToken)
        {
            var client = new Greeter.GreeterClient(channel);
            var request = client.EchoDuplex(deadline: DateTime.UtcNow.AddMinutes(10));
            var responses = new List<string>();
            var readTask = Task.Run(async () =>
            {
                await foreach (var response in request.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    responses.Add(response.Message);
                }
            });
            for (var i = 0; i < RequestCount; i++)
            {
                await request.RequestStream.WriteAsync(new EchoRequest() { Message = data }, cancellationToken);
                await Task.Delay(16);
            }

            // complete the request stream.
            await request.RequestStream.CompleteAsync().WaitAsync(cancellationToken);
            await readTask.WaitAsync(cancellationToken);

            return responses;
        }

        // Assert
        Assert.Equal(Concurrency, results.Length);
        Assert.All(results, x => Assert.Equal(RequestCount, x.Count));
        Assert.All(results, x => Assert.All(x, y => Assert.Equal(hash, SHA1.HashData(Encoding.UTF8.GetBytes(y)))));
    }
}