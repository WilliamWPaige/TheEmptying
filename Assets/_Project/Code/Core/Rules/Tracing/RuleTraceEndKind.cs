namespace TheEmptying.Core.Rules.Tracing
{
    /// <summary>How a run segment ended.</summary>
    public enum RuleTraceEndKind
    {
        Ended,
        Waiting,
        Asking,
        StepCap,
        ChainDepthCutOff,
        FireCap,
        Error
    }
}
