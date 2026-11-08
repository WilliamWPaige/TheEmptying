using System.Collections.Generic;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Reads a named numeric counter for an owner entity (defaulting to the kingdom); 0 if never set.</summary>
    public sealed class GetCounterBlockType : BlockType
    {
        public override string Id => "block.value.get_counter";
        public override string DisplayName => "Get Counter";
        public override string Category => "Memory";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("owner", PortDirection.Input, PortKind.Data, RuleDataType.Entity),
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Number)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("owner", RuleDataType.Entity, string.Empty),
            new FieldDefinition("name", RuleDataType.Text, string.Empty)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            EntityId owner = context.GetInput("owner").AsEntity;
            string name = context.GetInput("name").AsText;
            return RuleValue.Number(context.Memory.GetCounter(owner, name));
        }
    }
}
