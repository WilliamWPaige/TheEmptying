namespace TheEmptying.Core.Rules.Tracing
{
    /// <summary>
    /// What the rule runner reports a completed trace segment to. Attached
    /// per rule (RuleRunner.SetTraceListener); with no listener attached for
    /// a rule, the runner never builds a segment for it at all - no
    /// allocations, no extra work.
    /// </summary>
    public interface IRuleTraceListener
    {
        void OnSegmentCompleted(RuleTraceSegment segment);
    }
}
