using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Triggers at the start of every in-game month.</summary>
    public sealed class OnNewMonthTriggerBlockType : BlockType
    {
        public override string Id => "block.trigger.on_new_month";
        public override string DisplayName => "On New Month";
        public override string Category => "Triggers";
        public override BlockKind Kind => BlockKind.Trigger;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = Array.Empty<FieldDefinition>();

        public override RuleTriggerDescriptor DescribeTrigger(IReadOnlyDictionary<string, CompiledFieldValue> fields, SettingsRegistry settings)
        {
            return ScheduleTriggerDescriptor.OnNewMonth();
        }
    }
}
