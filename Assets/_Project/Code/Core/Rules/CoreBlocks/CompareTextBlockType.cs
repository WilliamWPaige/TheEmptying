using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Compares two strings ordinally, for equals or not equal.</summary>
    public sealed class CompareTextBlockType : BlockType
    {
        public override string Id => "block.value.compare_text";
        public override string DisplayName => "Compare Text";
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("a", PortDirection.Input, PortKind.Data, RuleDataType.Text, required: true),
            new PortDefinition("b", PortDirection.Input, PortKind.Data, RuleDataType.Text, required: true),
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Bool)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("operator", RuleDataType.Text, "=", allowedValues: new[] { "=", "!=" }),
            new FieldDefinition("a", RuleDataType.Text, string.Empty),
            new FieldDefinition("b", RuleDataType.Text, string.Empty)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            string a = context.GetInput("a").AsText;
            string b = context.GetInput("b").AsText;
            string op = context.GetInput("operator").AsText;

            bool equal = string.Equals(a, b, StringComparison.Ordinal);
            bool result = op == "!=" ? !equal : equal;

            return RuleValue.Bool(result);
        }
    }
}
