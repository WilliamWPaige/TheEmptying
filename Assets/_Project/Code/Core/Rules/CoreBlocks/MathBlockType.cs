using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Arithmetic with a field-selected operator: add, subtract, multiply, divide, min, max. Divide by zero gives 0 and writes a warning to the log.</summary>
    public sealed class MathBlockType : BlockType
    {
        public override string Id => "block.value.math";
        public override string DisplayName => "Math";
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("a", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("b", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Number)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("operator", RuleDataType.Text, "add"),
            new FieldDefinition("a", RuleDataType.Number, 0.0, allowsSettingReference: true),
            new FieldDefinition("b", RuleDataType.Number, 0.0, allowsSettingReference: true)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            double a = context.GetInput("a").AsNumber;
            double b = context.GetInput("b").AsNumber;
            string op = context.GetInput("operator").AsText;

            double result;
            switch (op)
            {
                case "add":
                    result = a + b;
                    break;
                case "subtract":
                    result = a - b;
                    break;
                case "multiply":
                    result = a * b;
                    break;
                case "divide":
                    if (b == 0)
                    {
                        context.Log("Math block: divide by zero; result is 0.");
                        result = 0;
                    }
                    else
                    {
                        result = a / b;
                    }

                    break;
                case "min":
                    result = System.Math.Min(a, b);
                    break;
                case "max":
                    result = System.Math.Max(a, b);
                    break;
                default:
                    result = 0;
                    break;
            }

            return RuleValue.Number(result);
        }
    }
}
