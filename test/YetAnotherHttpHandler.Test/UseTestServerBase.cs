using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

public abstract class UseTestServerTestBase : TimeoutTestBase
{
    protected ITestOutputHelper TestOutputHelper { get; }


    protected UseTestServerTestBase(ITestOutputHelper testOutputHelper)
    {
        TestOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
    }
}