using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace THNETII.Logging.DiagnosticsForwarding
{
    internal static class TraceEventTypeExtensions
    {
        internal static LogLevel ToLogLevel(this TraceEventType eventType) =>
            eventType switch
            {
                TraceEventType.Critical => LogLevel.Critical,
                TraceEventType.Error => LogLevel.Error,
                TraceEventType.Warning => LogLevel.Warning,
                TraceEventType.Information => LogLevel.Information,
                TraceEventType.Verbose => LogLevel.Debug,
                _ => LogLevel.Trace,
            };
    }
}
