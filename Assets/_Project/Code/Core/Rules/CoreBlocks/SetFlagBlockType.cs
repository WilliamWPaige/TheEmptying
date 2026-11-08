using System.Collections.Generic;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Writes a named true-or-false flag for an owner entity (defaulting to the kingdom).</summary>
    public sealed class SetFlagBlockType : BlockType
    {
        public override string Id => "block.action.set_flag";
        public override string DisplayName => "Set Flag";
        public override string Category => "Memory";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("owner", PortDirection.Input, PortKind.Data, RuleDataType.Entity),
            new PortDefinition("value", PortDirection.Input, PortKind.Data, RuleDataType.Bool, required: true),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("owner", RuleDataType.Entity, string.Empty),
            new FieldDefinition("name", RuleDataType.Text, string.Empty),
            new FieldDefinition("value", RuleDataType.Bool, false)
        };

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            EntityId owner = context.GetInput("owner").AsEntity;
            string name = context.GetInput("name").AsText;
            bool value = context.GetInput("value").AsBool;
            context.Memory.SetFlag(owner, name, value);
            return RuleExecutionResult.Follow("then");
        }
    }
}
