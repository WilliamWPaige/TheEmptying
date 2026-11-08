using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Triggers at the start of every in-game day.</summary>
    public sealed class OnNewDayTriggerBlockType : BlockType
    {
        public override string Id => "block.trigger.on_new_day";
        public override string DisplayName => "On New Day";
        public override string Category => "Triggers";
        public override BlockKind Kind => BlockKind.Trigger;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = Array.Empty<FieldDefinition>();

        public override RuleTriggerDescriptor DescribeTrigger(IReadOnlyDictionary<string, CompiledFieldValue> fields, SettingsRegistry settings)
        {
            return ScheduleTriggerDescriptor.OnNewDay();
        }
    }
}
