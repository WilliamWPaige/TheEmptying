using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Triggers when a named event is delivered.</summary>
    public sealed class OnEventTriggerBlockType : BlockType
    {
        public override string Id => "block.trigger.on_event";
        public override string DisplayName => "On Event";
        public override string Category => "Triggers";
        public override BlockKind Kind => BlockKind.Trigger;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("event", RuleDataType.Text, string.Empty, pickerKind: FieldPickerKind.EventId)
        };

        public override string GetTriggerEventDefinitionId(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            if (fieldValues != null && fieldValues.TryGetValue("event", out RuleFieldValueSource source) && !source.IsSettingReference)
            {
                return source.Literal?.Value<string>();
            }

            return null;
        }

        public override RuleTriggerDescriptor DescribeTrigger(IReadOnlyDictionary<string, CompiledFieldValue> fields, SettingsRegistry settings)
        {
            string eventId = RuleFieldResolver.Resolve(fields["event"], settings).AsText;
            return new EventTriggerDescriptor(eventId);
        }
    }
}
