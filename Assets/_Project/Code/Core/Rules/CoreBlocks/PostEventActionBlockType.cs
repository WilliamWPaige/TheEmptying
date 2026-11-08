using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Posts a named event, with up to three optional named parameters of any type.</summary>
    public sealed class PostEventActionBlockType : BlockType
    {
        public override string Id => "block.action.post_event";
        public override string DisplayName => "Post Event";
        public override string Category => "Actions";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("param1", PortDirection.Input, PortKind.Data),
            new PortDefinition("param2", PortDirection.Input, PortKind.Data),
            new PortDefinition("param3", PortDirection.Input, PortKind.Data),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("event", RuleDataType.Text, string.Empty, pickerKind: FieldPickerKind.EventId),
            new FieldDefinition("param1Name", RuleDataType.Text, string.Empty),
            new FieldDefinition("param2Name", RuleDataType.Text, string.Empty),
            new FieldDefinition("param3Name", RuleDataType.Text, string.Empty)
        };

        public override IEnumerable<string> GetPostedEventDefinitionIds(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            if (fieldValues != null && fieldValues.TryGetValue("event", out RuleFieldValueSource source) && !source.IsSettingReference)
            {
                string id = source.Literal?.Value<string>();
                if (!string.IsNullOrEmpty(id))
                {
                    yield return id;
                }
            }
        }

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            string eventId = context.GetInput("event").AsText;
            var parameters = new List<(string Name, RuleValue Value)>();

            for (int i = 1; i <= 3; i++)
            {
                string portName = "param" + i;
                if (!context.IsInputConnected(portName))
                {
                    continue;
                }

                string paramName = context.GetInput(portName + "Name").AsText;
                if (string.IsNullOrEmpty(paramName))
                {
                    continue;
                }

                parameters.Add((paramName, context.GetInput(portName)));
            }

            context.PostEvent(eventId, parameters);
            return RuleExecutionResult.Follow("then");
        }
    }
}
