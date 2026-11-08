using System.Collections.Generic;
using System.Globalization;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>Writes a formatted line to the Unity log (via the core's log sink), with up to three {0},{1},{2} placeholders.</summary>
    public sealed class WriteLogLineActionBlockType : BlockType
    {
        public override string Id => "block.action.write_log_line";
        public override string DisplayName => "Write Log Line";
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

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            context.Log(FormatText(context));
            return RuleExecutionResult.Follow("then");
        }

        internal static string FormatText(IRuleNodeContext context)
        {
            string format = context.GetInput("text").AsText;
            string arg0 = context.IsInputConnected("arg0") ? context.GetInput("arg0").ToString() : string.Empty;
            string arg1 = context.IsInputConnected("arg1") ? context.GetInput("arg1").ToString() : string.Empty;
            string arg2 = context.IsInputConnected("arg2") ? context.GetInput("arg2").ToString() : string.Empty;

            return string.Format(CultureInfo.InvariantCulture, format, arg0, arg1, arg2);
        }
    }
}
