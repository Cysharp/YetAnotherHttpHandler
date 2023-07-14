using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test.Helpers;

public class TestOutputLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;

    public TestOutputLoggerProvider(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
    }

    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(_testOutputHelper, categoryName);
    }

    private class Logger : ILogger
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly string _categoryName;

        public Logger(ITestOutputHelper testOutputHelper, string categoryName)
        {
            _testOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return AnonymousDisposable.Instance;
        }

        private class AnonymousDisposable : IDisposable
        {
            public static IDisposable Instance { get; } = new AnonymousDisposable();
            public void Dispose()
            {
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _testOutputHelper.WriteLine($"[{DateTime.Now}][{_categoryName}][{eventId}][{logLevel}] {formatter(state, exception)}");
            if (exception is not null)
            {
                _testOutputHelper.WriteLine(exception.ToString());
            }
        }
    }
}