using System.Collections.Generic;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Adds an amount (which may be negative) to a named numeric counter for an owner entity (defaulting to the kingdom).</summary>
    public sealed class ChangeCounterBlockType : BlockType
    {
        public override string Id => "block.action.change_counter";
        public override string DisplayName => "Change Counter";
        public override string Category => "Memory";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("owner", PortDirection.Input, PortKind.Data, RuleDataType.Entity),
            new PortDefinition("amount", PortDirection.Input, PortKind.Data, RuleDataType.Number, required: true),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("owner", RuleDataType.Entity, string.Empty),
            new FieldDefinition("name", RuleDataType.Text, string.Empty),
            new FieldDefinition("amount", RuleDataType.Number, 0.0, allowsSettingReference: true)
        };

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            EntityId owner = context.GetInput("owner").AsEntity;
            string name = context.GetInput("name").AsText;
            double amount = context.GetInput("amount").AsNumber;
            context.Memory.ChangeCounter(owner, name, amount);
            return RuleExecutionResult.Follow("then");
        }
    }
}
