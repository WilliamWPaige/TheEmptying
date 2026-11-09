using System.Collections.Generic;

namespace TheEmptying.Editor.Rules.Testing
{
    /// <summary>One numbered step in a run's plain-language view: a sentence saying what happened, the node it maps to on the canvas (null for the rare zero-step segment, which has no node to point at), and detail lines hidden behind a per-step "details" toggle.</summary>
    public sealed class RuleRunStep
    {
        public int Number { get; }
        public string NodeId { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Details { get; }

        public RuleRunStep(int number, string nodeId, string summary, IReadOnlyList<string> details)
        {
            Number = number;
            NodeId = nodeId;
            Summary = summary;
            Details = details;
        }
    }
}
