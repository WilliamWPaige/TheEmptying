using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>One node resolved to its block type, with every field resolved to a literal or a setting reference.</summary>
    public sealed class CompiledRuleNode
    {
        public string Id { get; }
        public BlockType BlockType { get; }
        public double X { get; }
        public double Y { get; }
        public IReadOnlyDictionary<string, CompiledFieldValue> Fields { get; }

        public CompiledRuleNode(string id, BlockType blockType, double x, double y, IReadOnlyDictionary<string, CompiledFieldValue> fields)
        {
            Id = id;
            BlockType = blockType;
            X = x;
            Y = y;
            Fields = fields;
        }
    }
}
