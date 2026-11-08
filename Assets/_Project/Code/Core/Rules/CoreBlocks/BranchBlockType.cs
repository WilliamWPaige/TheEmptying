using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    public sealed class BranchBlockType : BlockType
    {
        public override string Id => "block.flow.branch";
        public override string DisplayName => "Branch";
        public override string Category => "Flow";
        public override BlockKind Kind => BlockKind.Flow;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("condition", PortDirection.Input, PortKind.Data, RuleDataType.Bool, required: true),
            new PortDefinition("true", PortDirection.Output, PortKind.Execution),
            new PortDefinition("false", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("condition", RuleDataType.Bool, false)
        };

        public override string Execute(IRuleNodeContext context)
        {
            return context.GetInput("condition").AsBool ? "true" : "false";
        }
    }
}
