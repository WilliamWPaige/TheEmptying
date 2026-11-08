using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Core.Rules.CoreBlocks
{
    /// <summary>
    /// Shows the player a question with up to 4 choices, each its own
    /// execution output. Pauses the instance (reusing the paused-instance
    /// mechanism, with no resume tick) until the question is answered - by
    /// the player, a delegate, or a timeout.
    /// </summary>
    public sealed class AskPlayerBlockType : BlockType
    {
        public override string Id => "block.flow.ask_player";
        public override string DisplayName => "Ask Player";
        public override string Category => "Flow";
        public override BlockKind Kind => BlockKind.Flow;

        public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
        {
            new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
            new PortDefinition("arg0", PortDirection.Input, PortKind.Data),
            new PortDefinition("arg1", PortDirection.Input, PortKind.Data),
            new PortDefinition("arg2", PortDirection.Input, PortKind.Data),
            new PortDefinition("choice1", PortDirection.Output, PortKind.Execution),
            new PortDefinition("choice2", PortDirection.Output, PortKind.Execution),
            new PortDefinition("choice3", PortDirection.Output, PortKind.Execution),
            new PortDefinition("choice4", PortDirection.Output, PortKind.Execution)
        };

        public override IReadOnlyList<FieldDefinition> Fields { get; } = new[]
        {
            new FieldDefinition("category", RuleDataType.Text, "question.general"),
            new FieldDefinition("title", RuleDataType.Text, string.Empty),
            new FieldDefinition("body", RuleDataType.Text, string.Empty),
            new FieldDefinition("choice1", RuleDataType.Text, string.Empty),
            new FieldDefinition("choice2", RuleDataType.Text, string.Empty),
            new FieldDefinition("choice3", RuleDataType.Text, string.Empty),
            new FieldDefinition("choice4", RuleDataType.Text, string.Empty),
            new FieldDefinition("defaultChoice", RuleDataType.Number, 1.0),
            new FieldDefinition("timeoutHours", RuleDataType.Number, 0.0, allowsSettingReference: true)
        };

        public override IEnumerable<string> ValidateNode(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues, ISet<string> wiredExecutionOutputPorts)
        {
            var errors = new List<string>();
            var texts = new string[4];
            var known = new bool[4];

            for (int i = 0; i < 4; i++)
            {
                string fieldName = $"choice{i + 1}";
                known[i] = TryGetLiteralText(fieldValues, FindField(fieldName), out texts[i]);
            }

            int filledCount = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!known[i] || !string.IsNullOrEmpty(texts[i]))
                {
                    filledCount++;
                }
            }

            if (filledCount < 2)
            {
                errors.Add("Must have at least two choices filled in.");
            }

            for (int i = 0; i < 4; i++)
            {
                string portName = $"choice{i + 1}";
                if (wiredExecutionOutputPorts.Contains(portName) && known[i] && string.IsNullOrEmpty(texts[i]))
                {
                    errors.Add($"Choice output '{portName}' is wired, but its choice text is empty.");
                }
            }

            if (TryGetLiteralNumber(fieldValues, FindField("defaultChoice"), out double defaultChoiceNumber))
            {
                int defaultIndex = (int)Math.Round(defaultChoiceNumber, MidpointRounding.AwayFromZero) - 1;
                if (defaultIndex >= 0 && defaultIndex < 4 && known[defaultIndex] && string.IsNullOrEmpty(texts[defaultIndex]))
                {
                    errors.Add("The default choice points at an empty choice.");
                }
            }

            return errors;
        }

        public override RuleExecutionResult Execute(IRuleNodeContext context)
        {
            string category = context.GetInput("category").AsText;
            string title = context.GetInput("title").AsText;
            string body = FormatBody(context);

            var choiceTexts = new string[4];
            for (int i = 0; i < 4; i++)
            {
                choiceTexts[i] = context.GetInput($"choice{i + 1}").AsText;
            }

            int defaultChoiceNumber = (int)Math.Round(context.GetInput("defaultChoice").AsNumber, MidpointRounding.AwayFromZero);

            double timeoutHours = context.GetInput("timeoutHours").AsNumber;
            long timeoutTicks = timeoutHours > 0
                ? (long)Math.Round(timeoutHours * CalendarConstants.TicksPerHour, MidpointRounding.AwayFromZero)
                : 0;

            return RuleExecutionResult.Ask(category, title, body, choiceTexts, defaultChoiceNumber, timeoutTicks);
        }

        private static string FormatBody(IRuleNodeContext context)
        {
            string format = context.GetInput("body").AsText;
            string arg0 = context.IsInputConnected("arg0") ? context.GetInput("arg0").ToString() : string.Empty;
            string arg1 = context.IsInputConnected("arg1") ? context.GetInput("arg1").ToString() : string.Empty;
            string arg2 = context.IsInputConnected("arg2") ? context.GetInput("arg2").ToString() : string.Empty;

            return string.Format(CultureInfo.InvariantCulture, format, arg0, arg1, arg2);
        }

        /// <summary>True if the field's compile-time value could be determined (not a setting reference); text is the resolved (or default) text either way.</summary>
        private static bool TryGetLiteralText(IReadOnlyDictionary<string, RuleFieldValueSource> fields, FieldDefinition field, out string text)
        {
            text = field.DefaultValue as string ?? string.Empty;

            if (!fields.TryGetValue(field.Name, out RuleFieldValueSource source) || source == null)
            {
                return true;
            }

            if (source.IsSettingReference)
            {
                return false;
            }

            text = source.Literal == null || source.Literal.Type == JTokenType.Null ? string.Empty : source.Literal.Value<string>();
            return true;
        }

        private static bool TryGetLiteralNumber(IReadOnlyDictionary<string, RuleFieldValueSource> fields, FieldDefinition field, out double value)
        {
            value = field.DefaultValue is double d ? d : 0.0;

            if (!fields.TryGetValue(field.Name, out RuleFieldValueSource source) || source == null)
            {
                return true;
            }

            if (source.IsSettingReference)
            {
                return false;
            }

            value = source.Literal == null || source.Literal.Type == JTokenType.Null ? value : source.Literal.Value<double>();
            return true;
        }
    }
}
