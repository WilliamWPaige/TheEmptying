using System.Collections.Generic;
using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Testing
{
    /// <summary>A node's display name and block type Id, resolved once per rule - everything RuleTraceTextFormatter needs to phrase a step without touching the block registry itself.</summary>
    public readonly struct RuleTraceNodeInfo
    {
        public string DisplayName { get; }
        public string BlockTypeId { get; }

        public RuleTraceNodeInfo(string displayName, string blockTypeId)
        {
            DisplayName = displayName;
            BlockTypeId = blockTypeId;
        }

        /// <summary>Resolves every node in a rule to its display name and block type Id, for the trace formatter and the canvas highlighter's error label - one lookup per Build/Refresh rather than per entry.</summary>
        public static Dictionary<string, RuleTraceNodeInfo> BuildMap(RuleDefinition rule, BlockRegistry blocks)
        {
            var map = new Dictionary<string, RuleTraceNodeInfo>();
            if (rule?.Nodes == null)
            {
                return map;
            }

            foreach (RuleNode node in rule.Nodes)
            {
                string displayName = blocks != null && blocks.TryGet(node.BlockTypeId, out BlockType blockType) ? blockType.DisplayName : node.Id;
                map[node.Id] = new RuleTraceNodeInfo(displayName, node.BlockTypeId);
            }

            return map;
        }
    }
}
