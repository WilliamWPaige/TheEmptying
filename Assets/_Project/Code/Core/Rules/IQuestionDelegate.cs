namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// A module registers one of these with RuleRunner.RegisterQuestionDelegate
    /// to answer questions on the player's behalf (e.g. a lord answering by
    /// temperament). Delegates are tried in registration order (modules
    /// register in dependency order, so this is deterministic) for every
    /// question; the first to answer wins and the player is never asked. Each
    /// delegate decides from the question's own category whether it applies.
    /// </summary>
    public interface IQuestionDelegate
    {
        /// <summary>Return true and set choiceNumber (1 to 4, matching one of question.Choices) to answer; false to leave it for the next delegate or the player.</summary>
        bool TryAnswer(QuestionInfo question, out int choiceNumber);
    }
}
