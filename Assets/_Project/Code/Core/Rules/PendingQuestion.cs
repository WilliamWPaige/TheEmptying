using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Internal storage for one pending question: everything QuestionInfo
    /// exposes, plus the paused instance (if any - an ad-hoc question asked
    /// with no rule behind it has none) and the node that asked, so an answer
    /// can resolve which execution output to continue from.
    /// </summary>
    internal sealed class PendingQuestion
    {
        public long QuestionId;
        public string RuleId;
        public string AskingNodeId;
        public RuleInstance Instance;

        public string Category;
        public string Title;
        public string Body;

        /// <summary>All four slots; empty text (not null) means that choice is unused.</summary>
        public string[] ChoiceTexts = new string[4];

        public int DefaultChoiceNumber;
        public long TimeoutTick;
        public long TickAsked;

        public IReadOnlyList<(int ChoiceNumber, string Text)> FilledChoices()
        {
            var result = new List<(int, string)>();
            for (int i = 0; i < 4; i++)
            {
                if (!string.IsNullOrEmpty(ChoiceTexts[i]))
                {
                    result.Add((i + 1, ChoiceTexts[i]));
                }
            }

            return result;
        }

        public QuestionInfo ToInfo()
        {
            return new QuestionInfo(QuestionId, RuleId, Category, Title, Body, FilledChoices(), DefaultChoiceNumber, TickAsked);
        }
    }
}
