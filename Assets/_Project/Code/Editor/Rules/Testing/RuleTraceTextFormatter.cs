using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Editor.Rules.Testing
{
    /// <summary>
    /// Turns a trace segment into plain language for a designer, not a
    /// programmer: numbered steps (the trigger is step 1), one sentence each,
    /// times as in-game day and time rather than ticks, with field reads,
    /// computed values and node Ids tucked behind each step's own "details".
    /// Pure and UI-free, so every phrasing rule here is directly testable.
    /// </summary>
    public static class RuleTraceTextFormatter
    {
        public static string FormatDayTime(long tick)
        {
            long totalMinutes = tick / CalendarConstants.TicksPerMinute;
            int minute = (int)(totalMinutes % CalendarConstants.MinutesPerHour);

            long totalHours = totalMinutes / CalendarConstants.MinutesPerHour;
            int hour = (int)(totalHours % CalendarConstants.HoursPerDay);

            long totalDays = totalHours / CalendarConstants.HoursPerDay;
            long day = totalDays + 1;

            return $"Day {day}, {hour:00}:{minute:00}";
        }

        /// <summary>Every step of a run, in order: step 1 is how the segment started (a trigger firing, a resume, or an answer); each node that ran after that is its own step.</summary>
        public static IReadOnlyList<RuleRunStep> BuildSteps(RuleTraceSegment segment, IReadOnlyDictionary<string, RuleTraceNodeInfo> nodes)
        {
            if (segment == null)
            {
                return Array.Empty<RuleRunStep>();
            }

            nodes ??= new Dictionary<string, RuleTraceNodeInfo>();
            IReadOnlyList<RuleTraceEntry> entries = segment.Entries;
            var steps = new List<RuleRunStep>();

            bool isTriggerStart = segment.StartKind == RuleTraceStartKind.EventTrigger || segment.StartKind == RuleTraceStartKind.ScheduleTrigger;
            bool firstEntryIsTheTrigger = isTriggerStart && entries.Count > 0 && entries[0].Kind == RuleTraceEntryKind.NodeExecuted;

            string triggerDisplayName = firstEntryIsTheTrigger && nodes.TryGetValue(entries[0].NodeId, out RuleTraceNodeInfo triggerInfo) && !string.IsNullOrEmpty(triggerInfo.DisplayName)
                ? triggerInfo.DisplayName
                : null;

            List<string> startDetails = segment.StartKind == RuleTraceStartKind.EventTrigger
                ? segment.StartEventParameters.Select(p => $"{p.Name} = {p.ValueText}").ToList()
                : new List<string>();

            steps.Add(new RuleRunStep(1, firstEntryIsTheTrigger ? entries[0].NodeId : null, DescribeStart(segment, triggerDisplayName), startDetails));

            int startIndex = firstEntryIsTheTrigger ? 1 : 0;
            int number = 2;
            var pendingDetails = new List<RuleTraceEntry>();

            for (int i = startIndex; i < entries.Count; i++)
            {
                RuleTraceEntry entry = entries[i];
                if (entry.Kind != RuleTraceEntryKind.NodeExecuted)
                {
                    pendingDetails.Add(entry);
                    continue;
                }

                nodes.TryGetValue(entry.NodeId, out RuleTraceNodeInfo info);
                bool isLastEntry = i == entries.Count - 1;
                string summary = DescribeNodeStep(entry, info, pendingDetails, segment, isLastEntry);
                List<string> detailLines = pendingDetails.Select(d => FormatDetail(d, nodes)).ToList();

                steps.Add(new RuleRunStep(number, entry.NodeId, summary, detailLines));
                number++;
                pendingDetails = new List<RuleTraceEntry>();
            }

            return steps;
        }

        /// <summary>The one plain sentence describing how the segment ended.</summary>
        public static string DescribeEnd(RuleTraceSegment segment, IReadOnlyDictionary<string, RuleTraceNodeInfo> nodes)
        {
            if (segment == null)
            {
                return string.Empty;
            }

            switch (segment.EndKind)
            {
                case RuleTraceEndKind.Ended:
                    return "Finished.";
                case RuleTraceEndKind.Waiting:
                    return $"Waiting until {FormatDayTime(segment.EndResumeTick)}.";
                case RuleTraceEndKind.Asking:
                    return "Waiting for an answer.";
                case RuleTraceEndKind.StepCap:
                    return "Stopped: ran too many steps without finishing.";
                case RuleTraceEndKind.ChainDepthCutOff:
                    return "Stopped: too many rules chained together.";
                case RuleTraceEndKind.FireCap:
                    return "Stopped: this rule already fired its limit for now.";
                case RuleTraceEndKind.Error:
                    string name = NameOf(segment.EndNodeId, nodes);
                    return $"Stopped: error in {name}: {segment.EndErrorMessage}";
                default:
                    return segment.EndKind.ToString();
            }
        }

        private static string DescribeStart(RuleTraceSegment segment, string triggerDisplayName)
        {
            switch (segment.StartKind)
            {
                case RuleTraceStartKind.EventTrigger:
                    return $"{triggerDisplayName ?? "On Event"} fired: {segment.StartEventDefinitionId}";
                case RuleTraceStartKind.ScheduleTrigger:
                    return $"{triggerDisplayName ?? "Schedule"} fired.";
                case RuleTraceStartKind.Resume:
                    return $"Resumed at {FormatDayTime(segment.StartResumeTick)}.";
                case RuleTraceStartKind.Answer:
                    return $"Continued after answering question {segment.StartQuestionId} with choice {segment.StartChoiceNumber}.";
                default:
                    return segment.StartKind.ToString();
            }
        }

        private static string DescribeNodeStep(RuleTraceEntry entry, RuleTraceNodeInfo info, List<RuleTraceEntry> details, RuleTraceSegment segment, bool isLastEntry)
        {
            string displayName = string.IsNullOrEmpty(info.DisplayName) ? entry.NodeId : info.DisplayName;
            string blockTypeId = info.BlockTypeId ?? string.Empty;

            if (blockTypeId == "block.action.write_log_line" || blockTypeId == "block.action.write_herald_line")
            {
                string message = FindMessage(details, entry.NodeId, RuleTraceEntryKind.LineLogged);
                return message != null ? $"{displayName} wrote: {message}" : $"{displayName} ran.";
            }

            if (blockTypeId == "block.action.post_event")
            {
                string eventId = FindMessage(details, entry.NodeId, RuleTraceEntryKind.EventPosted);
                return eventId != null ? $"{displayName} posted {eventId}" : $"{displayName} ran.";
            }

            if (blockTypeId == "block.flow.branch")
            {
                return entry.PortOrFieldName != null ? $"{displayName} went: {entry.PortOrFieldName}" : $"{displayName} ran.";
            }

            if (blockTypeId == "block.flow.wait" && isLastEntry && segment.EndKind == RuleTraceEndKind.Waiting)
            {
                return $"{displayName} paused until {FormatDayTime(segment.EndResumeTick)}";
            }

            if (blockTypeId == "block.flow.ask_player" && isLastEntry && segment.EndKind == RuleTraceEndKind.Asking)
            {
                string title = FindInputRead(details, entry.NodeId, "title");
                return !string.IsNullOrEmpty(title)
                    ? $"{displayName} asked '{title}' and is waiting for an answer"
                    : $"{displayName} is waiting for an answer";
            }

            if (blockTypeId.StartsWith("block.action.set_local.", StringComparison.Ordinal))
            {
                RuleTraceEntry localSet = details.FirstOrDefault(d => d.Kind == RuleTraceEntryKind.LocalSet && d.NodeId == entry.NodeId);
                return localSet != null ? $"Set Local set {localSet.PortOrFieldName} = {localSet.Value}" : $"{displayName} ran.";
            }

            return $"{displayName} ran.";
        }

        private static string FindMessage(List<RuleTraceEntry> details, string nodeId, RuleTraceEntryKind kind)
        {
            return details.FirstOrDefault(d => d.Kind == kind && d.NodeId == nodeId)?.Message;
        }

        private static string FindInputRead(List<RuleTraceEntry> details, string nodeId, string portOrFieldName)
        {
            RuleTraceEntry entry = details.FirstOrDefault(d => d.Kind == RuleTraceEntryKind.InputRead && d.NodeId == nodeId && d.PortOrFieldName == portOrFieldName);
            return entry?.Value.HasValue == true ? entry.Value.Value.ToString() : null;
        }

        private static string FormatDetail(RuleTraceEntry entry, IReadOnlyDictionary<string, RuleTraceNodeInfo> nodes)
        {
            string name = NameOf(entry.NodeId, nodes);
            return entry.Kind switch
            {
                RuleTraceEntryKind.ValueComputed => $"{name} computed {entry.Value}",
                RuleTraceEntryKind.InputRead => $"{name}.{entry.PortOrFieldName} read {entry.Value}",
                RuleTraceEntryKind.LocalSet => $"{name} set local '{entry.PortOrFieldName}' = {entry.Value}",
                RuleTraceEntryKind.EventPosted => $"{name} posted '{entry.Message}'",
                RuleTraceEntryKind.LineLogged => $"{name} logged: {entry.Message}",
                _ => entry.ToString()
            };
        }

        private static string NameOf(string nodeId, IReadOnlyDictionary<string, RuleTraceNodeInfo> nodes)
        {
            if (nodeId == null)
            {
                return "a node";
            }

            return nodes != null && nodes.TryGetValue(nodeId, out RuleTraceNodeInfo info) && !string.IsNullOrEmpty(info.DisplayName)
                ? info.DisplayName
                : nodeId;
        }
    }
}
