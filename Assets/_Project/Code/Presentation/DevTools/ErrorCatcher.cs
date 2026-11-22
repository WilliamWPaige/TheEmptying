using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>
    /// Collects every Unity warning, error and exception from the moment the
    /// game starts (installed via RuntimeInitializeOnLoadMethod, so it runs
    /// before any scene, including before the debug window ever opens).
    /// Plain Debug.Log messages are never collected.
    /// </summary>
    public static class ErrorCatcher
    {
        private static readonly List<DebugLogEntry> Entries_ = new List<DebugLogEntry>();
        private static readonly Dictionary<(DebugLogType Type, string Message), DebugLogEntry> Index = new Dictionary<(DebugLogType, string), DebugLogEntry>();

        /// <summary>Raised whenever a new entry is added or an existing one's count changes.</summary>
        public static event Action Changed;

        /// <summary>Set once a simulation exists, so new entries can carry the in-game date and time. Entries collected before this is set show "-".</summary>
        public static Func<string> InGameTimestampProvider { get; set; }

        public static IReadOnlyList<DebugLogEntry> Entries => Entries_;

        public static int ErrorCount => Entries_.Where(e => e.Type != DebugLogType.Warning).Sum(e => e.Count);
        public static int WarningCount => Entries_.Where(e => e.Type == DebugLogType.Warning).Sum(e => e.Count);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Entries_.Clear();
            Index.Clear();
            InGameTimestampProvider = null;
            Application.logMessageReceived -= OnLogMessageReceived;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            DebugLogType? mapped = Map(type);
            if (mapped == null)
            {
                return;
            }

            Record(mapped.Value, message, stackTrace);
        }

        /// <summary>Separated from the Unity callback so grouping can be exercised directly.</summary>
        public static void Record(DebugLogType type, string message, string stackTrace)
        {
            var key = (type, message);
            if (Index.TryGetValue(key, out DebugLogEntry existing))
            {
                existing.Count++;
            }
            else
            {
                string firstLine = string.IsNullOrEmpty(stackTrace) ? string.Empty : stackTrace.Split('\n')[0].TrimEnd('\r');
                var entry = new DebugLogEntry(type, message, firstLine, stackTrace ?? string.Empty, InGameTimestampProvider?.Invoke() ?? "-", DateTime.Now);
                Index[key] = entry;
                Entries_.Add(entry);
            }

            Changed?.Invoke();
        }

        public static void Clear()
        {
            Entries_.Clear();
            Index.Clear();
            Changed?.Invoke();
        }

        private static DebugLogType? Map(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return DebugLogType.Warning;
                case LogType.Error:
                case LogType.Assert:
                    return DebugLogType.Error;
                case LogType.Exception:
                    return DebugLogType.Exception;
                default:
                    return null;
            }
        }
    }
}
