using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Runs up to four execution outputs in order, each to completion, before ending.</summary>
    public sealed class SequenceBlockType : BlockType
    {
        public override string Id => "block.flow.sequence";
        public override string DisplayName => "Sequence";
        public override string Category => "Flow";
        public override BlockKind Kind => BlockKind.Flow;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("then1", PortDirection.Output, PortKind.Execution),
            new PortDefinition("then2", PortDirection.Output, PortKind.Execution),
            new PortDefinition("then3", PortDirection.Output, PortKind.Execution),
            new PortDefinition("then4", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = Array.Empty<FieldDefinition>();

        private static readonly string[] OutputPorts = { "then1", "then2", "then3", "then4" };

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            return RuleExecutionResult.FollowInOrder(OutputPorts);
        }
    }
}
