using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    public sealed class OrBlockType : BlockType
    {
        public override string Id => "block.value.or";
        public override string DisplayName => "Or";
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("a", PortDirection.Input, PortKind.Data, RuleDataType.Bool, required: true),
            new PortDefinition("b", PortDirection.Input, PortKind.Data, RuleDataType.Bool, required: true),
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Bool)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("a", RuleDataType.Bool, false),
            new FieldDefinition("b", RuleDataType.Bool, false)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            return RuleValue.Bool(context.GetInput("a").AsBool || context.GetInput("b").AsBool);
        }
    }
}
