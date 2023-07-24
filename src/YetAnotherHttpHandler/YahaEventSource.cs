using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text;

namespace Cysharp.Net.Http
{
    [EventSource(Name = "Cysharp.Net.Http.YetAnotherHttpHandler")]
    internal class YahaEventSource : EventSource
    {
        public const int EventIdTrace = 10;
        public const int EventIdInfo = 20;
        public const int EventIdWarning = 30;
        public const int EventIdError = 40;

        public static YahaEventSource Log { get; } = new YahaEventSource();
        public bool IsEnabled { get; set; }
#if DEBUG
            = true;
#endif

        [Event(EventIdTrace, Level = EventLevel.Verbose, Message = "{0}")]
        public void Trace(string message)
        {
            Debug.Assert(IsEnabled);
            WriteEvent(EventIdTrace, message);
        }

        [Event(EventIdInfo, Level = EventLevel.Informational, Message = "{0}")]
        public void Info(string message)
        {
            Debug.Assert(IsEnabled);
            WriteEvent(EventIdInfo, message);
        }

        [Event(EventIdWarning, Level = EventLevel.Warning, Message = "{0}")]
        public void Warning(string message)
        {
            Debug.Assert(IsEnabled);
            WriteEvent(EventIdWarning, message);
        }

        [Event(EventIdError, Level = EventLevel.Error, Message = "{0}")]
        public void Error(string message)
        {
            Debug.Assert(IsEnabled);
            WriteEvent(EventIdError, message);
        }
    }
}
