namespace TheEmptying.Core.Rules.Tracing
{
    /// <summary>What started a run segment.</summary>
    public enum RuleTraceStartKind
    {
        EventTrigger,
        ScheduleTrigger,
        Resume,
        Answer
    }
}
