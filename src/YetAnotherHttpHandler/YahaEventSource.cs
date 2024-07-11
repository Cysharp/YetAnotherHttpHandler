using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text;

namespace Cysharp.Net.Http
{
#if UNITY_2021_1_OR_NEWER
    internal class YahaEventSource
    {
        public bool IsEnabled()
#if YAHA_ENABLE_DEBUG_TRACING
            => true;
#else
            => false;
#endif

        public static YahaEventSource Log { get; } = new();

        public void Trace(string message)
        {
            UnityEngine.Debug.Log(message);
        }

        public void Info(string message)
        {
            UnityEngine.Debug.Log(message);
        }

        public void Warning(string message)
        {
            UnityEngine.Debug.LogWarning(message);
        }

        public void Error(string message)
        {
            UnityEngine.Debug.LogError(message);
        }
    }
#else
    [EventSource(Name = "Cysharp.Net.Http.YetAnotherHttpHandler")]
    internal class YahaEventSource : EventSource
    {
        public const int EventIdTrace = 10;
        public const int EventIdInfo = 20;
        public const int EventIdWarning = 30;
        public const int EventIdError = 40;

        public static YahaEventSource Log { get; } = new();

        [Event(EventIdTrace, Level = EventLevel.Verbose, Message = "{0}")]
        public void Trace(string message)
        {
            Debug.Assert(IsEnabled());
            WriteEvent(EventIdTrace, message);
        }

        [Event(EventIdInfo, Level = EventLevel.Informational, Message = "{0}")]
        public void Info(string message)
        {
            Debug.Assert(IsEnabled());
            WriteEvent(EventIdInfo, message);
        }

        [Event(EventIdWarning, Level = EventLevel.Warning, Message = "{0}")]
        public void Warning(string message)
        {
            Debug.Assert(IsEnabled());
            WriteEvent(EventIdWarning, message);
        }

        [Event(EventIdError, Level = EventLevel.Error, Message = "{0}")]
        public void Error(string message)
        {
            Debug.Assert(IsEnabled());
            WriteEvent(EventIdError, message);
        }
    }
#endif
}
