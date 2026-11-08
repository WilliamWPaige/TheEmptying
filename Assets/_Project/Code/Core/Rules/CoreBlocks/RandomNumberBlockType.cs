using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>A uniformly random number in [min, max), from the "rules" random stream.</summary>
    public sealed class RandomNumberBlockType : BlockType
    {
        public override string Id => "block.value.random_number";
        public override string DisplayName => "Random Number";
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("min", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("max", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Number)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("min", RuleDataType.Number, 0.0, allowsSettingReference: true),
            new FieldDefinition("max", RuleDataType.Number, 1.0, allowsSettingReference: true)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            double min = context.GetInput("min").AsNumber;
            double max = context.GetInput("max").AsNumber;
            float t = context.Random.NextFloat();
            return RuleValue.Number(min + (t * (max - min)));
        }
    }
}
