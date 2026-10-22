using System.IO.Pipelines;
using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

[Collection(nameof(YetAnotherHttpHandlerTest))]
public class DisposeTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    protected override TimeSpan UnexpectedTimeout => TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Dispose_ReleasesRequestsThatFailBeforeNativeWorkStarts()
    {
        using var handler = new NativeHttpHandlerCore(new NativeClientSettings());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("relative", UriKind.Relative));
        // Call the core directly so URI conversion fails after native request
        // allocation, instead of HttpClient rejecting it before allocation.
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.SendAsync(request, CancellationToken.None));

        var runtime = NativeRuntime.Instance.Acquire();
        NativeRuntime.Instance.Release();
        handler.Dispose();

        // A failed, unregistered request must not keep the runtime alive until
        // GC finalizes its SafeHandle. No GC.Collect is needed for disposal.
        Assert.True(runtime.IsClosed);
    }

    [Fact]
    public async Task Dispose_CancelsPendingHeaders_WhileAnotherHandlerKeepsWorking()
    {
        await using var server = await LaunchServerAsync(TestServerListenMode.InsecureHttp1Only);
        using var handler = new YetAnotherHttpHandler();
        using var client = new HttpClient(handler, disposeHandler: false);
        using var otherHandler = new YetAnotherHttpHandler();
        using var otherClient = new HttpClient(otherHandler, disposeHandler: false);
        Assert.Equal("__OK__", await otherClient.GetStringAsync(server.BaseUri, TimeoutToken));

        var response = client.GetAsync($"{server.BaseUri}/slow-response-headers", TimeoutToken);
        await _DisposeOnDedicatedThread(handler).WaitAsync(TimeoutToken);

        // HttpClient's managed continuations can run after native work stops.
        await _AssertRequestStoppedAsync(response);
        Assert.Equal("__OK__", await otherClient.GetStringAsync(server.BaseUri, TimeoutToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync(server.BaseUri, TimeoutToken));
        handler.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_UnblocksUnreadResponseBody(bool http2)
    {
        await using var server = await LaunchServerAsync(http2
            ? TestServerListenMode.InsecureHttp2Only : TestServerListenMode.InsecureHttp1Only);
        using var handler = new YetAnotherHttpHandler
        {
            Http2Only = http2,
            ResponsePipeOptions = new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1, useSynchronizationContext: false),
        };
        using var client = new HttpClient(handler, disposeHandler: false);
        // No request cancellation token: Dispose itself must unblock the flush.
        using var response = await client.GetAsync($"{server.BaseUri}/random?size=1048576", HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken);
        using var body = await response.Content.ReadAsStreamAsync(TimeoutToken);
        Assert.Equal(1, await body.ReadAsync(new byte[1], TimeoutToken));

        await _DisposeOnDedicatedThread(handler).WaitAsync(TimeoutToken);

        await _AssertRequestStoppedAsync(body.CopyToAsync(Stream.Null, TimeoutToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_DoesNotWaitForFlushContinuationOnDisposingThread(bool useWriterScheduler)
    {
        await using var server = await LaunchServerAsync(TestServerListenMode.InsecureHttp1Only);
        var disposingThread = new DisposingThreadQueue();
        using var handler = new YetAnotherHttpHandler
        {
            ResponsePipeOptions = useWriterScheduler
                ? new PipeOptions(writerScheduler: disposingThread, pauseWriterThreshold: 1, resumeWriterThreshold: 1, useSynchronizationContext: false)
                : new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1, useSynchronizationContext: true),
        };
        using var client = new HttpClient(handler, disposeHandler: false);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(disposingThread);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"{server.BaseUri}/random?size=1048576", HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken).ConfigureAwait(false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        using var _ = response;
        using var body = await response.Content.ReadAsStreamAsync(TimeoutToken);
        Assert.Equal(1, await body.ReadAsync(new byte[1], TimeoutToken));
        // Let OnReceive hit backpressure and defer its acknowledgement.
        await Task.Delay(200, TimeoutToken);

        // The flush continuation is posted to the disposing thread, which is
        // blocked in Dispose and cannot run it until Dispose returns.
        var dispose = Task.Factory.StartNew(() =>
        {
            SynchronizationContext.SetSynchronizationContext(disposingThread);
            handler.Dispose();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            // Unblock a deadlocked Dispose so a failure does not hang the run.
            disposingThread.DrainOnThreadPool();
        }

        await _AssertRequestStoppedAsync(body.CopyToAsync(Stream.Null, TimeoutToken));
    }

    private sealed class DisposingThreadQueue : PipeScheduler
    {
        private readonly object _lock = new();
        private readonly List<(Action<object?> Action, object? State)> _queue = new();
        private bool _drained;

        public override void Schedule(Action<object?> action, object? state)
        {
            lock (_lock)
            {
                if (!_drained)
                {
                    _queue.Add((action, state));
                    return;
                }
            }
            System.Threading.ThreadPool.UnsafeQueueUserWorkItem(action, state, preferLocal: false);
        }

        public void DrainOnThreadPool()
        {
            lock (_lock)
            {
                _drained = true;
                foreach (var (action, state) in _queue)
                {
                    System.Threading.ThreadPool.UnsafeQueueUserWorkItem(action, state, preferLocal: false);
                }
                _queue.Clear();
            }
        }

        public static implicit operator SynchronizationContext(DisposingThreadQueue queue) => new QueueSynchronizationContext(queue);
    }

    private sealed class QueueSynchronizationContext(DisposingThreadQueue queue) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => queue.Schedule(s => d(s), state);
        public override SynchronizationContext CreateCopy() => this;
    }

    [Fact]
    public async Task Dispose_UnblocksUploadToServerThatDoesNotRead()
    {
        await using var server = await LaunchServerAsync(TestServerListenMode.InsecureHttp2Only);
        using var handler = new YetAnotherHttpHandler
        {
            Http2Only = true,
        };
        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUri}/post-never-read")
        {
            Content = new ByteArrayContent(new byte[8 * 1024 * 1024]),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeoutToken);

        await _DisposeOnDedicatedThread(handler).WaitAsync(TimeoutToken);

        await _AssertRequestStoppedAsync(response.Content.CopyToAsync(Stream.Null, TimeoutToken));
    }

    [Fact]
    public async Task Dispose_WaitsForCertificateCallback_AndConcurrentDispose()
    {
        await using var server = await LaunchServerAsync(TestServerListenMode.SecureHttp2Only);
        using var callbackRelease = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackReturned = false;
        using var handler = new YetAnotherHttpHandler
        {
            OnVerifyServerCertificate = (_, _, _) =>
            {
                callbackEntered.TrySetResult();
                callbackRelease.Wait(TimeoutToken);
                callbackReturned = true;
                return true;
            },
        };
        using var client = new HttpClient(handler, disposeHandler: false);
        var response = client.GetAsync(server.BaseUri);
        Task? first = null;
        Task? second = null;
        try
        {
            await callbackEntered.Task.WaitAsync(TimeoutToken);
            first = _DisposeOnDedicatedThread(handler);
            // Cancellation of the pending response proves Dispose has started.
            await _AssertRequestStoppedAsync(response);
            Assert.False(first.IsCompleted);
            second = _DisposeOnDedicatedThread(handler);
            // The first Dispose is held inside the verifier. A second caller
            // must not return early just because the public handler is closed.
            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(100, TimeoutToken)));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync(server.BaseUri));
        }
        finally
        {
            callbackRelease.Set();
            if (first != null)
            {
                await first.WaitAsync(TimeoutToken);
            }
            if (second != null)
            {
                await second.WaitAsync(TimeoutToken);
            }
        }
        Assert.True(callbackReturned);
    }

    [Fact]
    public async Task Dispose_FromCertificateCallback_IsRejectedWithoutDisposingHandler()
    {
        await using var server = await LaunchServerAsync(TestServerListenMode.SecureHttp2Only);
        using var handler = new YetAnotherHttpHandler();
        Exception? disposalError = null;
        handler.OnVerifyServerCertificate = (_, _, _) =>
        {
            disposalError = Record.Exception(handler.Dispose);
            return true;
        };
        using var client = new HttpClient(handler, disposeHandler: false);

        Assert.Equal("__OK__", await client.GetStringAsync(server.BaseUri, TimeoutToken));
        Assert.IsType<InvalidOperationException>(disposalError);
        Assert.Equal("__OK__", await client.GetStringAsync(server.BaseUri, TimeoutToken));
        await _DisposeOnDedicatedThread(handler).WaitAsync(TimeoutToken);
    }

    [Fact]
    public async Task Dispose_RacesWithRequestAdmission()
    {
        await using var server = await LaunchServerAsync(TestServerListenMode.InsecureHttp1Only);
        for (var iteration = 0; iteration < 30; iteration++)
        {
            using var handler = new YetAnotherHttpHandler();
            using var client = new HttpClient(handler, disposeHandler: false);
            using var start = new ManualResetEventSlim();
            var sends = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                start.Wait(TimeoutToken);
                try
                {
                    using var response = await client.GetAsync(server.BaseUri, TimeoutToken);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex) when (ex is ObjectDisposedException or HttpRequestException or OperationCanceledException)
                {
                    // Either admitted and cancelled, or rejected at admission.
                }
            })).ToArray();
            var dispose = Task.Run(() =>
            {
                start.Wait(TimeoutToken);
                handler.Dispose();
            });
            start.Set();
            await Task.WhenAll(sends.Append(dispose)).WaitAsync(TimeoutToken);
        }
    }

    private static Task _DisposeOnDedicatedThread(YetAnotherHttpHandler handler)
    {
        return Task.Factory.StartNew(handler.Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static async Task _AssertRequestStoppedAsync(Task request)
    {
        var error = await Record.ExceptionAsync(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error is HttpRequestException or OperationCanceledException or IOException,
            $"Expected request cancellation/failure, got {error}");
    }
}
