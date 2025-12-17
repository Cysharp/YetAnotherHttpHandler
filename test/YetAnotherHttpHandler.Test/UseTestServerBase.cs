using HttpClientTestServer.Launcher;
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

    protected virtual TestServerOptions ConfigureServerOptions(TestServerOptions options)
        => options;

    protected Task<ITestServer> LaunchServerAsync(TestServerListenMode listenMode)
        => LaunchServerAsync(ConfigureServerOptions(TestServerOptions.CreateFromListenMode(listenMode)));

    protected async Task<ITestServer> LaunchServerAsync(TestServerOptions serverOptions)
    {
        return await InProcessTestServer.LaunchAsync(
            ConfigureServerOptions(serverOptions),
            new TestOutputLoggerProvider(TestOutputHelper),
            TimeoutToken
        );
    }

    public void Dispose()
    {
        _tokenRegistration.Dispose();
    }
}
