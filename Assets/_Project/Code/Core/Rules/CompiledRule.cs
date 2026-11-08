using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>The fast internal form of one compiled rule. Prompt 2.2 adds execution; this only resolves structure.</summary>
    public sealed class CompiledRule
    {
        public string Id { get; }
        public string Name { get; }

        /// <summary>The Id of the rule's single trigger node. Never null for a rule that compiled without errors.</summary>
        public string TriggerNodeId { get; }

        public IReadOnlyDictionary<string, CompiledRuleNode> Nodes { get; }
        public IReadOnlyList<CompiledRuleConnection> Connections { get; }

        public CompiledRule(string id, string name, string triggerNodeId, IReadOnlyDictionary<string, CompiledRuleNode> nodes, IReadOnlyList<CompiledRuleConnection> connections)
        {
            Id = id;
            Name = name;
            TriggerNodeId = triggerNodeId;
            Nodes = nodes;
            Connections = connections;
        }
    }
}
