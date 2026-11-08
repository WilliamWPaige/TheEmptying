using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Plain data: an instance's whole execution position. CurrentNodeId is
    /// the node to run next (null when there's nothing queued at this level).
    /// ContinuationStack holds what remains to run once the current sub-path
    /// ends. No recursion holds any of this state, so it can be saved and
    /// resumed exactly.
    /// </summary>
    public sealed class RuleExecutionPosition
    {
        public string CurrentNodeId { get; set; }
        public List<RuleContinuationFrame> ContinuationStack { get; } = new List<RuleContinuationFrame>();
    }
}
