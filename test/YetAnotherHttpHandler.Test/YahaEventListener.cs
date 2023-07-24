using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace _YetAnotherHttpHandler.Test;

internal class YahaEventListener : EventListener
{
    private static YahaEventListener _eventListener;
    private const string ProviderName = "Cysharp.Net.Http.YetAnotherHttpHandler";

    [ModuleInitializer]
    public static void Initialize()
    {
        _eventListener = new YahaEventListener();
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == ProviderName)
        {
            EnableEvents(eventSource, EventLevel.Verbose);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        Debug.WriteLine($"{eventData.EventSource.Name}: {eventData.Level}: {string.Format(eventData.Message ?? string.Empty, eventData.Payload?.ToArray() ?? Array.Empty<object>())}");
    }
}

internal class YahaXunitEventListener : EventListener
{
    private static ITestOutputHelper _testOutputHelper;
    private const string ProviderName = "Cysharp.Net.Http.YetAnotherHttpHandler";

    public YahaXunitEventListener(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == ProviderName)
        {
            EnableEvents(eventSource, EventLevel.Verbose);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        _testOutputHelper.WriteLine($"[{DateTime.Now}][{eventData.EventSource.Name}][{eventData.Level}] {string.Format(eventData.Message ?? string.Empty, eventData.Payload?.ToArray() ?? Array.Empty<object>())}");
    }
}