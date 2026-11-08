using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    public sealed class StopBlockType : BlockType
    {
        public override string Id => "block.flow.stop";
        public override string DisplayName => "Stop";
        public override string Category => "Flow";
        public override BlockKind Kind => BlockKind.Flow;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = Array.Empty<FieldDefinition>();

        public override RuleExecutionResult Execute(IRuleNodeContext context) => RuleExecutionResult.End;
    }
}
