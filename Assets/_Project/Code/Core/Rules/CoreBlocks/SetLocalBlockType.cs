using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Writes a named local variable from its value input. Locals live only for the current instance; one value port type per instance.</summary>
    public sealed class SetLocalBlockType : BlockType
    {
        private readonly RuleDataType _type;

        public override string Id { get; }
        public override string DisplayName { get; }
        public override string Category => "Memory";
        public override BlockKind Kind => BlockKind.Action;
        public override IReadOnlyList<PortDefinition> Ports { get; }
        public override IReadOnlyList<FieldDefinition> Fields { get; }

        public SetLocalBlockType(RuleDataType type)
        {
            _type = type;
            string suffix = type.ToString().ToLowerInvariant();
            Id = $"block.action.set_local.{suffix}";
            DisplayName = $"Set Local ({type})";
            Ports = new[]
            {
                new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
                new PortDefinition("value", PortDirection.Input, PortKind.Data, type, required: true),
                new PortDefinition("then", PortDirection.Output, PortKind.Execution)
            };
            Fields = new[]
            {
                new FieldDefinition("name", RuleDataType.Text, string.Empty),
                new FieldDefinition("value", type, DefaultValueFor(type))
            };
        }

        public override string Execute(IRuleNodeContext context)
        {
            string name = context.GetInput("name").AsText;
            RuleValue value = context.GetInput("value");
            context.SetLocal(name, _type, value);
            return "then";
        }

        private static object DefaultValueFor(RuleDataType type)
        {
            return type switch
            {
                RuleDataType.Number => 0.0,
                RuleDataType.Bool => false,
                RuleDataType.Entity => string.Empty,
                _ => string.Empty
            };
        }
    }
}
