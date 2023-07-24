using _YetAnotherHttpHandler.Test.Helpers;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public abstract class UseTestServerTestBase : TimeoutTestBase, IDisposable
{
    private readonly CancellationTokenRegistration _tokenRegistration;

    protected ITestOutputHelper TestOutputHelper { get; }

    protected UseTestServerTestBase(ITestOutputHelper testOutputHelper)
    {
        TestOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
        _tokenRegistration = TimeoutToken.Register(() =>
        {
            testOutputHelper.WriteLine($"[{DateTime.Now}][{nameof(TimeoutTestBase)}] Timeout reached");
        });
    }

    protected async Task<TestWebAppServer> LaunchAsync<T>(TestWebAppServerListenMode listenMode)
        where T : ITestServerBuilder
    {
        return await TestWebAppServer.LaunchAsync<T>(listenMode, TestOutputHelper, TimeoutToken);
    }

    public void Dispose()
    {
        _tokenRegistration.Dispose();
    }
}