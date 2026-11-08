namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// A module registers one of these with RuleRunner.RegisterQuestionDelegate
    /// to answer questions on the player's behalf (e.g. a lord answering by
    /// temperament). Delegates are tried in Category order (then registration
    /// order) for every question; the first to answer wins and the player is
    /// never asked.
    /// </summary>
    public interface IQuestionDelegate
    {
        /// <summary>Used only to order delegates deterministically (ordinal), not to filter which questions this delegate sees.</summary>
        string Category { get; }

        /// <summary>Return true and set choiceNumber (1 to 4, matching one of question.Choices) to answer; false to leave it for the next delegate or the player.</summary>
        bool TryAnswer(QuestionInfo question, out int choiceNumber);
    }
}
