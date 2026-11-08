using System.Collections.Generic;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Triggers every N in-game hours.</summary>
    public sealed class EveryNHoursTriggerBlockType : BlockType
    {
        public override string Id => "block.trigger.every_n_hours";
        public override string DisplayName => "Every N Hours";
        public override string Category => "Triggers";
        public override BlockKind Kind => BlockKind.Trigger;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("hours", RuleDataType.Number, 1.0, allowsSettingReference: true)
        };

        public override RuleTriggerDescriptor DescribeTrigger(IReadOnlyDictionary<string, CompiledFieldValue> fields, SettingsRegistry settings)
        {
            int hours = (int)RuleFieldResolver.Resolve(fields["hours"], settings).AsNumber;
            return ScheduleTriggerDescriptor.EveryNHours(hours);
        }
    }
}
