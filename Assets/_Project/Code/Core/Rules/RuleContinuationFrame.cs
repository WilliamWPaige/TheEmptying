using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Plain data: a node waiting to run more of its execution outputs once
    /// the current sub-path ends (e.g. a Sequence's remaining outputs).
    /// Saved as part of a paused instance's position.
    /// </summary>
    public sealed class RuleContinuationFrame
    {
        public string NodeId { get; set; }
        public List<string> RemainingPorts { get; set; }

        public RuleContinuationFrame()
        {
        }

        public RuleContinuationFrame(string nodeId, List<string> remainingPorts)
        {
            NodeId = nodeId;
            RemainingPorts = remainingPorts;
        }
    }
}
