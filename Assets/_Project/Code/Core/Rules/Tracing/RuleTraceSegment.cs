using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.Tracing
{
    /// <summary>
    /// One run segment's complete record: from a trigger, resume or answer
    /// until the instance ends, waits, asks, or stops (step cap, chain-depth
    /// cutoff, fire cap or error). Immutable once built by RuleTraceRecorder.
    /// </summary>
    public sealed class RuleTraceSegment
    {
        public string RuleId { get; }

        /// <summary>0 for a segment that never got as far as creating an instance (chain-depth cutoff, fire cap).</summary>
        public long InstanceId { get; }

        public long Tick { get; }

        public RuleTraceStartKind StartKind { get; }
        public string StartEventDefinitionId { get; }
        public IReadOnlyList<(string Name, string ValueText)> StartEventParameters { get; }
        public long StartResumeTick { get; }
        public long StartQuestionId { get; }
        public int StartChoiceNumber { get; }

        public IReadOnlyList<RuleTraceEntry> Entries { get; }

        public RuleTraceEndKind EndKind { get; }
        public long EndResumeTick { get; }
        public long EndQuestionId { get; }
        public string EndErrorMessage { get; }

        /// <summary>The node an error happened on. Null for every other end kind.</summary>
        public string EndNodeId { get; }

        internal RuleTraceSegment(
            string ruleId, long instanceId, long tick,
            RuleTraceStartKind startKind, string startEventDefinitionId, IReadOnlyList<(string Name, string ValueText)> startEventParameters, long startResumeTick, long startQuestionId, int startChoiceNumber,
            IReadOnlyList<RuleTraceEntry> entries,
            RuleTraceEndKind endKind, long endResumeTick, long endQuestionId, string endErrorMessage, string endNodeId)
        {
            RuleId = ruleId;
            InstanceId = instanceId;
            Tick = tick;
            StartKind = startKind;
            StartEventDefinitionId = startEventDefinitionId;
            StartEventParameters = startEventParameters ?? Array.Empty<(string, string)>();
            StartResumeTick = startResumeTick;
            StartQuestionId = startQuestionId;
            StartChoiceNumber = startChoiceNumber;
            Entries = entries ?? Array.Empty<RuleTraceEntry>();
            EndKind = endKind;
            EndResumeTick = endResumeTick;
            EndQuestionId = endQuestionId;
            EndErrorMessage = endErrorMessage;
            EndNodeId = endNodeId;
        }

        public override string ToString()
        {
            string start = StartKind switch
            {
                RuleTraceStartKind.EventTrigger => $"event '{StartEventDefinitionId}'",
                RuleTraceStartKind.ScheduleTrigger => "schedule",
                RuleTraceStartKind.Resume => $"resume at tick {StartResumeTick}",
                RuleTraceStartKind.Answer => $"answer to question {StartQuestionId} (choice {StartChoiceNumber})",
                _ => StartKind.ToString()
            };

            string end = EndKind switch
            {
                RuleTraceEndKind.Waiting => $"waiting until tick {EndResumeTick}",
                RuleTraceEndKind.Asking => $"asking question {EndQuestionId}",
                RuleTraceEndKind.Error => $"error: {EndErrorMessage}",
                _ => EndKind.ToString()
            };

            return $"[{RuleId} #{InstanceId} t{Tick}] {start} -> {Entries.Count} entries -> {end}";
        }
    }
}
