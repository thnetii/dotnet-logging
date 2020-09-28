using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

using Microsoft.Extensions.Logging;

namespace THNETII.Logging.DiagnosticsForwarding
{
    public class TraceSourceLogForwarder : TraceListener
    {
        private readonly ILogger logger;
        private readonly ReaderWriterLockSlim eventIdLock =
            new ReaderWriterLockSlim();
        private readonly Dictionary<string, int> categoryToEventId =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object lineBufferSync = new object();
        private EventId lineBufferEventId;
        private readonly StringBuilder lineBufferMessage = new StringBuilder();
        private readonly List<IDisposable> lineBufferScopes =
            new List<IDisposable>();

        public TraceSourceLogForwarder(ILoggerFactory loggerFactory,
            string? name = null) : base(name)
        {
            loggerFactory ??= Microsoft.Extensions.Logging.Abstractions
                .NullLoggerFactory.Instance;

            logger = loggerFactory.CreateLogger(name);
        }

        public override void Write(object o)
        {
            lock (lineBufferSync)
            { WriteInternal(o); }
        }

        public override void Write(object o, string category)
        {
            lock (lineBufferSync)
            { WriteInternal(o, category); }
        }

        public override void Write(string message)
        {
            lock (lineBufferSync)
            { WriteInternal(message); }
        }

        public override void Write(string message, string category)
        {
            lock (lineBufferSync)
            { WriteInternal(message, category); }
        }

        public override void WriteLine(object o)
        {
            lock (lineBufferSync)
            { WriteLineInternal(o); }
        }

        public override void WriteLine(object o, string category)
        {
            lock (lineBufferSync)
            { WriteLineInternal(o, category); }
        }

        public override void WriteLine(string message)
        {
            lock (lineBufferSync)
            { WriteLineInternal(message); }
        }

        public override void WriteLine(string message, string category)
        {
            lock (lineBufferSync)
            { WriteLineInternal(message, category); }
        }

        private void WriteInternal(object o)
        {
            lineBufferScopes.Add(logger.BeginScope(o));
            if (o?.ToString() is string message)
                WriteInternal(message);
        }

        private void WriteInternal(object o, string category)
        {
            SetLineBufferCategory(category);
            WriteInternal(o);
        }

        private void WriteInternal(string message)
        {
            lineBufferMessage.Append(message);
        }

        private void WriteInternal(string message, string category)
        {
            SetLineBufferCategory(category);
            WriteInternal(category + ": " + message);
        }

        private void WriteLineInternal(object o)
        {
            lineBufferScopes.Add(logger.BeginScope(o));
            if (o?.ToString() is string message)
                WriteLineInternal(message);
        }

        private void WriteLineInternal(object o, string category)
        {
            SetLineBufferCategory(category);
            WriteLineInternal(o);
        }

        private void WriteLineInternal(string message)
        {
            WriteInternal(message);
            Flush();
        }

        private void WriteLineInternal(string message, string category)
        {
            SetLineBufferCategory(category);
            WriteLineInternal(message);
        }

        private void SetLineBufferCategory(string category)
        {
            if (lineBufferEventId.Id == 0 && !string.IsNullOrEmpty(category))
                lineBufferEventId = GetEventId(category);
            else if (!lineBufferEventId.Name.Equals(category, StringComparison.OrdinalIgnoreCase))
                lineBufferEventId = GetEventId(lineBufferEventId.Name + '+' + category);
        }

        public override void Fail(string message)
        {
            logger.LogCritical(message);
        }

        public override void Fail(string message, string detailMessage)
        {
            string messageTrimmed = message?.TrimEnd() ?? string.Empty;
            const string detailArgs = "{" + nameof(detailMessage) + "}";
            string messageFormat = messageTrimmed.Length > 0
                ? messageTrimmed + "; " + detailArgs
                : detailArgs;
            logger.LogCritical(messageFormat, detailMessage);
        }

        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
        {
            var eventId = new EventId(id, source);
            var logLevel = eventType.ToLogLevel();
            logger.Log(logLevel, eventId, state: data, exception: null,
                (state, except) => state?.ToString() ?? string.Empty);
        }

        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, params object[] data)
        {
            data ??= Array.Empty<object>();

            var eventId = new EventId(id, source);
            var logLevel = eventType.ToLogLevel();
            using var scope = logger.BeginScope(eventCache);
            logger.Log(logLevel, eventId, state: data, exception: null,
                (state, except) => string.Join(", ", state));
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id) =>
            TraceEvent(eventCache, source, eventType, id, string.Empty);

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            var eventId = new EventId(id, source);
            var logLevel = eventType.ToLogLevel();
            using var scope = logger.BeginScope(eventCache);
            logger.Log(logLevel, eventId, message);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            var eventId = new EventId(id, source);
            var logLevel = eventType.ToLogLevel();
            using var scope = logger.BeginScope(eventCache);
            logger.Log(logLevel, eventId, format, args);
        }

        public override void TraceTransfer(TraceEventCache eventCache, string source, int id, string message, Guid relatedActivityId)
        {
            TraceEvent(eventCache, source, TraceEventType.Transfer, id, message + ", {" + nameof(relatedActivityId) + "}", relatedActivityId);
        }

        private EventId GetEventId(string category)
        {
            if (string.IsNullOrEmpty(category))
                return default;
            bool hasEventId;
            int eventId;
            eventIdLock.EnterReadLock();
            try
            {
                hasEventId = categoryToEventId.TryGetValue(category, out eventId);
            }
            finally { eventIdLock.ExitReadLock(); }

            if (!hasEventId)
            {
                eventIdLock.EnterWriteLock();
                try
                {
                    hasEventId = categoryToEventId.TryGetValue(category, out eventId);
                    if (!hasEventId)
                    {
                        eventId = categoryToEventId.Count + 1;
                        categoryToEventId[category] = eventId;
                    }
                }
                finally { eventIdLock.ExitWriteLock(); }
            }

            return new EventId(eventId, category);
        }

        public override void Flush()
        {
            try
            {
                string message = lineBufferMessage.ToString();
                logger.LogDebug(lineBufferEventId, message);
            }
            finally
            {
                foreach (IDisposable scope in lineBufferScopes)
                {
                    scope.Dispose();
                }

                lineBufferEventId = default;
                lineBufferScopes.Clear();
                lineBufferMessage.Clear();
            }

            base.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                eventIdLock.Dispose();
                if (lineBufferScopes.Count > 0 || lineBufferMessage.Length > 0)
                    Flush();
            }

            base.Dispose(disposing);
        }
    }
}
