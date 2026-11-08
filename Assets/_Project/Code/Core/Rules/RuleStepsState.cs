namespace TheEmptying.Core.Rules
{
    /// <summary>Shared across one instance's whole run (including nested RunExecutionOutput sub-runs), so the step cap applies to the instance as a whole.</summary>
    internal sealed class RuleStepsState
    {
        public int Count;
        public bool CapReported;
    }
}
