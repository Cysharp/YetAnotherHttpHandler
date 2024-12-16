using System.Diagnostics;

namespace _YetAnotherHttpHandler.Test;

public abstract class TimeoutTestBase
{
    private readonly CancellationTokenSource _timeoutTokenSource;

    protected CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : _timeoutTokenSource.Token;

    protected virtual TimeSpan UnexpectedTimeout => Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10);
    protected DateTimeOffset UnexpectedTimeoutOn { get; }


    protected TimeoutTestBase()
    {
        _timeoutTokenSource = new CancellationTokenSource(UnexpectedTimeout);
        UnexpectedTimeoutOn = DateTimeOffset.UtcNow.Add(UnexpectedTimeout);
    }
}