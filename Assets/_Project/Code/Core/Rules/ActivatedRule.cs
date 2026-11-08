using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>A compiled rule, activated: its trigger descriptor, the node it starts at, and precomputed connection indexes keyed by port name.</summary>
    internal sealed class ActivatedRule
    {
        public CompiledRule Rule { get; }
        public RuleTriggerDescriptor Trigger { get; set; }
        public string TriggerNextNodeId { get; set; }

        public Dictionary<(string Node, string Port), string> ExecOutputTargets { get; } = new Dictionary<(string, string), string>();
        public Dictionary<(string Node, string Port), CompiledRuleConnection> DataInputConnections { get; } = new Dictionary<(string, string), CompiledRuleConnection>();

        /// <summary>Unsubscribes the event subscription or unregisters the scheduler entry. Called when the rule is switched off after a runtime error.</summary>
        public Action Deactivate { get; set; }

        public bool IsActive { get; set; } = true;

        public ActivatedRule(CompiledRule rule)
        {
            Rule = rule;
        }
    }
}
