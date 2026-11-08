namespace TheEmptying.Core.Rules
{
    /// <summary>One runtime issue: a chain depth cutoff, a per-tick fire cap, a step cap, or (in Release mode) a runtime error that switched the rule off.</summary>
    public sealed class RuleRuntimeReport
    {
        public string RuleId { get; }
        public string NodeId { get; }
        public string Message { get; }
        public long Tick { get; }

        public RuleRuntimeReport(string ruleId, string nodeId, string message, long tick)
        {
            RuleId = ruleId;
            NodeId = nodeId;
            Message = message;
            Tick = tick;
        }

        public override string ToString()
        {
            return $"[{RuleId}/{NodeId ?? "-"}] tick {Tick}: {Message}";
        }
    }
}
