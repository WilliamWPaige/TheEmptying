using System;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>
    /// One collected Unity log message. Repeated identical (type, message)
    /// pairs increment Count on the same entry instead of adding a new one.
    /// </summary>
    public sealed class DebugLogEntry
    {
        public DebugLogType Type { get; }
        public string Message { get; }
        public string FirstStackLine { get; }
        public string FullStackTrace { get; }

        /// <summary>The in-game date and time when first seen, or "-" if no simulation existed yet.</summary>
        public string InGameTimestamp { get; }

        public DateTime RealTimestamp { get; }

        public int Count { get; set; }

        public DebugLogEntry(DebugLogType type, string message, string firstStackLine, string fullStackTrace, string inGameTimestamp, DateTime realTimestamp)
        {
            Type = type;
            Message = message;
            FirstStackLine = firstStackLine;
            FullStackTrace = fullStackTrace;
            InGameTimestamp = inGameTimestamp;
            RealTimestamp = realTimestamp;
            Count = 1;
        }
    }
}
