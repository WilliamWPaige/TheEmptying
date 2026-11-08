using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>A read-only view of one pending question, for delegates and the presentation layer.</summary>
    public sealed class QuestionInfo
    {
        public long QuestionId { get; }
        public string RuleId { get; }
        public string Category { get; }
        public string Title { get; }
        public string Body { get; }

        /// <summary>The filled-in choices only, each with its 1-based choice number (1 to 4) and text.</summary>
        public IReadOnlyList<(int ChoiceNumber, string Text)> Choices { get; }

        public int DefaultChoiceNumber { get; }
        public long TickAsked { get; }

        public QuestionInfo(long questionId, string ruleId, string category, string title, string body, IReadOnlyList<(int ChoiceNumber, string Text)> choices, int defaultChoiceNumber, long tickAsked)
        {
            QuestionId = questionId;
            RuleId = ruleId;
            Category = category;
            Title = title;
            Body = body;
            Choices = choices;
            DefaultChoiceNumber = defaultChoiceNumber;
            TickAsked = tickAsked;
        }
    }
}
