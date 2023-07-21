using _YetAnotherHttpHandler.Test.Helpers;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public abstract class UseTestServerTestBase : TimeoutTestBase
{
    protected ITestOutputHelper TestOutputHelper { get; }

    protected UseTestServerTestBase(ITestOutputHelper testOutputHelper)
    {
        TestOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
    }

    protected async Task<TestWebAppServer> LaunchAsync<T>(TestWebAppServerListenMode listenMode)
        where T : ITestServerBuilder
    {
        return await TestWebAppServer.LaunchAsync<T>(listenMode, TestOutputHelper, TimeoutToken);
    }
}