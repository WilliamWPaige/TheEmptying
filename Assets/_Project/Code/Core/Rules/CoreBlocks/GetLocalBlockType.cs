using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Reads a named local variable. Locals live only for the current instance; one output port type per instance.</summary>
    public sealed class GetLocalBlockType : BlockType
    {
        private readonly RuleDataType _type;

        public override string Id { get; }
        public override string DisplayName { get; }
        public override string Category => "Memory";
        public override BlockKind Kind => BlockKind.Value;
        public override IReadOnlyList<PortDefinition> Ports { get; }
        public override IReadOnlyList<FieldDefinition> Fields { get; }

        public GetLocalBlockType(RuleDataType type)
        {
            _type = type;
            string suffix = type.ToString().ToLowerInvariant();
            Id = $"block.value.get_local.{suffix}";
            DisplayName = $"Get Local ({type})";
            Ports = new[] { new PortDefinition("result", PortDirection.Output, PortKind.Data, type) };
            Fields = new[] { new FieldDefinition("name", RuleDataType.Text, string.Empty) };
        }

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            string name = context.GetInput("name").AsText;
            return context.GetLocal(name, _type);
        }
    }
}
