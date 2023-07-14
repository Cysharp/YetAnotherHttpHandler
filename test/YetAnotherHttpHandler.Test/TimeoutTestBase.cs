using System.Diagnostics;

namespace _YetAnotherHttpHandler.Test;

public abstract class TimeoutTestBase
{
    private readonly CancellationTokenSource _timeoutTokenSource;

    protected CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : _timeoutTokenSource.Token;

    protected virtual TimeSpan UnexpectedTimeout => Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10);


    protected TimeoutTestBase()
    {
        _timeoutTokenSource = new CancellationTokenSource(UnexpectedTimeout);
    }
}