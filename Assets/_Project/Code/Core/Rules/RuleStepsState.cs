namespace TheEmptying.Core.Rules
{
    /// <summary>Lives only for one call to RuleRunner.RunFrom (one run segment, from a trigger or a resume to its next end or wait), so the step cap applies to that whole segment.</summary>
    internal sealed class RuleStepsState
    {
        public int Count;
        public bool CapReported;
    }
}
