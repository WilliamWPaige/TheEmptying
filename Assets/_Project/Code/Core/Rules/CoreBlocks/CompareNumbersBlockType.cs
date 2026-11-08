using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Compares two numbers with a field-selected operator: =, !=, &lt;, &lt;=, &gt;, &gt;=.</summary>
    public sealed class CompareNumbersBlockType : BlockType
    {
        public override string Id => "block.value.compare_numbers";
        public override string DisplayName => "Compare Numbers";
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("a", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("b", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Bool)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("operator", RuleDataType.Text, "="),
            new FieldDefinition("a", RuleDataType.Number, 0.0, allowsSettingReference: true),
            new FieldDefinition("b", RuleDataType.Number, 0.0, allowsSettingReference: true)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            double a = context.GetInput("a").AsNumber;
            double b = context.GetInput("b").AsNumber;
            string op = context.GetInput("operator").AsText;

            bool result = op switch
            {
                "=" => a == b,
                "!=" => a != b,
                "<" => a < b,
                "<=" => a <= b,
                ">" => a > b,
                ">=" => a >= b,
                _ => false
            };

            return RuleValue.Bool(result);
        }
    }
}
