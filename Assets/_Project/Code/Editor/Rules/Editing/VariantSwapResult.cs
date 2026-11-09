using System.Collections.Generic;
using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>One editing operation's outcome for a variant swap: which wires (if any) no longer fit the new variant and were removed.</summary>
    public sealed class VariantSwapResult
    {
        public bool Succeeded { get; }
        public IReadOnlyList<RuleConnection> RemovedConnections { get; }

        public VariantSwapResult(bool succeeded, IReadOnlyList<RuleConnection> removedConnections)
        {
            Succeeded = succeeded;
            RemovedConnections = removedConnections;
        }
    }
}
