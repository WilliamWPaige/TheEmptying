using System.Collections.Generic;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Triggers once a day, at a given hour (0 to 23).</summary>
    public sealed class AtHourOfDayTriggerBlockType : BlockType
    {
        public override string Id => "block.trigger.at_hour_of_day";
        public override string DisplayName => "At Hour Of Day";
        public override string Category => "Triggers";
        public override BlockKind Kind => BlockKind.Trigger;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("hour", RuleDataType.Number, 0.0, allowsSettingReference: true)
        };

        public override RuleTriggerDescriptor DescribeTrigger(IReadOnlyDictionary<string, CompiledFieldValue> fields, SettingsRegistry settings)
        {
            int hour = (int)RuleFieldResolver.Resolve(fields["hour"], settings).AsNumber;
            return ScheduleTriggerDescriptor.AtHourOfDay(hour);
        }
    }
}
