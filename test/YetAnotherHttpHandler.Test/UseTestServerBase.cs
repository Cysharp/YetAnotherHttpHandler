using Microsoft.AspNetCore.Builder;

namespace _YetAnotherHttpHandler.Test;

public abstract class UseTestServerTestBase : TimeoutTestBase, IDisposable
{
    private readonly CancellationTokenRegistration _tokenRegistration;

    protected ITestOutputHelper TestOutputHelper { get; }

    protected UseTestServerTestBase(ITestOutputHelper testOutputHelper)
    {
        TestOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
        testOutputHelper.WriteLine($"[{DateTime.Now}][{nameof(TimeoutTestBase)}] UnexpectedTimeout = {UnexpectedTimeout} ({UnexpectedTimeoutOn})");
        _tokenRegistration = TimeoutToken.Register(() =>
        {
            testOutputHelper.WriteLine($"[{DateTime.Now}][{nameof(TimeoutTestBase)}] Timeout reached");
        });
    }

    protected async Task<TestWebAppServer> LaunchServerAsync<T>(TestWebAppServerListenMode listenMode, Action<WebApplicationBuilder>? configure = null)
        where T : ITestServerBuilder
    {
        return await TestWebAppServer.LaunchAsync<T>(listenMode, TestOutputHelper, TimeoutToken, configure);
    }

    public void Dispose()
    {
        _tokenRegistration.Dispose();
    }
}