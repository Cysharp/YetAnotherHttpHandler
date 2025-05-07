using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class BackPressureTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    private readonly ITestOutputHelper _testOutputHelper = testOutputHelper;

    [Theory]
    [InlineData(4186)] // pass
    [InlineData(65535)] // pass
    [InlineData(65536)] // fail
    [InlineData(131972)] // fail
    public async Task FlushTest(int size)
    {
        using var httpHandler = new YetAnotherHttpHandler();
        using var client = new HttpClient(httpHandler);
        await using var server = await LaunchServerAsync<TestServerForHttp1AndHttp2>(TestWebAppServerListenMode.InsecureHttp1Only);

        var numRequests = Environment.ProcessorCount + 1; // more than the number of tokio worker threads

        _testOutputHelper.WriteLine($"NumRequests: {numRequests}");

        var pendingStreams = new List<Stream>();

        for (var i = 0; i < numRequests; i++)
        {
            _testOutputHelper.WriteLine($"Request #{i}");

            using var timeout = new CancellationTokenSource();
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var stream = await client.GetStreamAsync($"{server.BaseUri}/random?size={size}", timeout.Token);

            pendingStreams.Add(stream);
        }

        foreach (var stream in pendingStreams) await stream.DisposeAsync();
    }
}