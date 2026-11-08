using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Reads a named setting's current value as a number, live every time it computes.</summary>
    public sealed class ReadSettingBlockType : BlockType
    {
        public override string Id => "block.value.read_setting";
        public override string DisplayName => "Read Setting";
        public override string Category => "Values";
        public override BlockKind Kind => BlockKind.Value;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("result", PortDirection.Output, PortKind.Data, RuleDataType.Number)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("setting", RuleDataType.Text, string.Empty)
        };

        public override RuleValue ComputeValue(IRuleNodeContext context)
        {
            string settingId = context.GetInput("setting").AsText;
            return RuleValue.Number(context.Settings.Get(settingId));
        }
    }
}
