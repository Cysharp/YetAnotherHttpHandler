using Cysharp.Net.Http;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;

namespace _YetAnotherHttpHandler.Test;

[CollectionDefinition(nameof(YetAnotherHttpHandlerTest), DisableParallelization = true)]
public class YetAnotherHttpHandlerTestCollection;

[Collection(nameof(YetAnotherHttpHandlerTest))]
public class YetAnotherHttpHandlerTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    [Fact]
    public async Task Disposed()
    {
        // Arrange
        var handler = new YetAnotherHttpHandler();
        handler.Dispose();
        var httpClient = new HttpClient(handler);

        // Act
        var ex = await Record.ExceptionAsync(async () => await httpClient.GetStringAsync("http://localhost"));

        // Assert
        Assert.IsType<ObjectDisposedException>(ex);
    }

    [Fact]
    public async Task DisposeHandler_During_SendBuffer_Is_Full()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Pre-condition
        Assert.Equal(0, NativeRuntime.Instance._refCount);

        var runtimeHandle = NativeRuntime.Instance.Acquire();

        // To prevent references remaining from local variables, make it a local function.
        async Task RunAsync()
        {
            // Arrange
            var httpHandler = new YetAnotherHttpHandler() { Http2Only = true };
            var httpClient = new HttpClient(httpHandler);
            await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp2Only);

            // Act
            var pipe = new Pipe();
            var writeTask = Task.Run(async () =>
            {
                var buffer = new byte[1024 * 1024];
                while (true)
                {
                    await pipe.Writer.WriteAsync(buffer);
                }
            });
            var content = new StreamContent(pipe.Reader.AsStream());
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-never-read")
            {
                Version = HttpVersion.Version20,
                Content = content,
            };
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken);
            await Task.Delay(TimeSpan.FromSeconds(5)); // Wait for the send buffer to overflow.

            // Decrease the reference count of manually held internal handles for direct observation.
            NativeRuntime.Instance.Release();

            // Dispose all related resources.
            request.Dispose();
            response.Dispose();
            httpHandler.Dispose();
            httpClient.Dispose();
        }

        await RunAsync();

        // Run Finalizer.
        await Task.Delay(100);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(100);

        // Assert
        Assert.Equal(0, NativeRuntime.Instance._refCount);
        Assert.True(runtimeHandle.IsClosed);
    }

    // NOTE: Currently, this test can only be run on Windows.
    [Fact]
    [OSSkipCondition(OperatingSystems.MacOSX | OperatingSystems.Linux)]
    public async Task InitializationFailure()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Pre-condition
        Assert.Equal(0, NativeRuntime.Instance._refCount);

        using var handler = new YetAnotherHttpHandler() { UnixDomainSocketPath = "/path/to/socket" };
        using var httpClient = new HttpClient(handler);
        // Throw an exception on initialization.
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await httpClient.GetStringAsync("http://localhost:1"));

        // Handler and HttpClient are disposed here. Reference count of NativeRuntime should be 0.
        Assert.Equal(0, NativeRuntime.Instance._refCount);
    }

    // NOTE: Currently, this test can only be run on Windows.
    [Fact(Skip = "Due to the state remaining from Http2Test.Cancel_Post_SendingBody_Duplex, this test fails. Enable it after fixing that issue.")]
    public async Task SetWorkerThreads()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Pre-condition
        Assert.Equal(0, NativeRuntime.Instance._refCount);
        {
            var tokioThreads = ThreadEnumerator.GetThreadsWithNames(Process.GetCurrentProcess().Id).Count(x => x.ThreadName.Contains("tokio-runtime-worker"));
            Assert.Equal(0, tokioThreads);
        }

        // Set the number of worker threads to (Number of cores * 2).
        var workerThreads = Environment.ProcessorCount * 2;
        YetAnotherHttpHandler.SetWorkerThreads(workerThreads);

        using (var handler = new YetAnotherHttpHandler())
        using (var httpClient = new HttpClient(handler))
        {
            try
            {
                // Send a request to initialize the runtime.
                await httpClient.GetStringAsync("http://127.0.0.1");
            }
            catch
            {
                // Ignore
            }
            finally
            {
                // Revert the number of worker threads to default.
                YetAnotherHttpHandler.SetWorkerThreads(null);
            }

            var tokioThreads = ThreadEnumerator.GetThreadsWithNames(Process.GetCurrentProcess().Id).Count(x => x.ThreadName.Contains("tokio-runtime-worker"));
            Assert.Equal(workerThreads, tokioThreads);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        {
            var tokioThreads = ThreadEnumerator.GetThreadsWithNames(Process.GetCurrentProcess().Id).Count(x => x.ThreadName.Contains("tokio-runtime-worker"));
            Assert.Equal(0, tokioThreads);
        }

        // Handler and HttpClient are disposed here. Reference count of NativeRuntime should be 0.
        Assert.Equal(0, NativeRuntime.Instance._refCount);
    }

}