using System.Collections.Generic;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>
    /// Same text formatting as Write Log Line, but also posts the data event
    /// "event.herald_line" (Public, notability 5) with the formatted text, for
    /// the herald and chronicle to pick up once they exist. Also writes to the log.
    /// </summary>
    public sealed class WriteHeraldLineActionBlockType : BlockType
    {
        public const string EventDefinitionId = "event.herald_line";

        public override string Id => "block.action.write_herald_line";
        public override string DisplayName => "Write Herald Line";
        public override string Category => "Actions";
        public override BlockKind Kind => BlockKind.Action;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("arg0", PortDirection.Input, PortKind.Data),
            new PortDefinition("arg1", PortDirection.Input, PortKind.Data),
            new PortDefinition("arg2", PortDirection.Input, PortKind.Data),
            new PortDefinition("then", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("text", RuleDataType.Text, string.Empty)
        };

        public override IEnumerable<string> GetPostedEventDefinitionIds(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            yield return EventDefinitionId;
        }

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            string text = WriteLogLineActionBlockType.FormatText(context);
            context.Log(text);
            context.PostEvent(EventDefinitionId, new[] { ("text", RuleValue.Text(text)) });
            return RuleExecutionResult.Follow("then");
        }
    }
}
