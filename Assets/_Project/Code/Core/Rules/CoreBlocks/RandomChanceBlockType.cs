using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>A true/false roll against a probability (0 to 1), from the "rules" random stream.</summary>
    public sealed class RandomChanceBlockType : BlockType
    {
        public override string Id => "block.value.random_chance";
        public override string DisplayName => "Random Chance";
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("probability", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Bool)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("probability", RuleDataType.Number, 0.5, allowsSettingReference: true)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            double probability = context.GetInput("probability").AsNumber;
            return RuleValue.Bool(context.Random.Chance((float)probability));
        }
    }
}
