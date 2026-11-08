using System.Collections.Generic;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Sets a named numeric counter for an owner entity (defaulting to the kingdom) to a value.</summary>
    public sealed class SetCounterBlockType : BlockType
    {
        public override string Id => "block.action.set_counter";
        public override string DisplayName => "Set Counter";
        public override string Category => "Memory";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("owner", PortDirection.Input, PortKind.Data, RuleDataType.Entity),
            new PortDefinition("value", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("owner", RuleDataType.Entity, string.Empty),
            new FieldDefinition("name", RuleDataType.Text, string.Empty),
            new FieldDefinition("value", RuleDataType.Number, 0.0, allowsSettingReference: true)
        };

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            EntityId owner = context.GetInput("owner").AsEntity;
            string name = context.GetInput("name").AsText;
            double value = context.GetInput("value").AsNumber;
            context.Memory.SetCounter(owner, name, value);
            return RuleExecutionResult.Follow("then");
        }
    }
}
