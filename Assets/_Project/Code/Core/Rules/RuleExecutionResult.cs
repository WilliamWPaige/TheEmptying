using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>What a block's Execute asks the runner to do next. Only the factory methods are public; the runner itself is the only thing that inspects which kind this is.</summary>
    public abstract class RuleExecutionResult
    {
        public static readonly RuleExecutionResult End = new EndExecutionResult();

        /// <summary>Follow one execution output port.</summary>
        public static RuleExecutionResult Follow(string portName) => new FollowExecutionResult(portName);

        /// <summary>Follow several execution output ports in order, each run to completion before the next starts (e.g. Sequence).</summary>
        public static RuleExecutionResult FollowInOrder(IReadOnlyList<string> portNames) => new FollowInOrderExecutionResult(portNames);

        /// <summary>Pause the instance until resumeTick, then continue from resumePortName's target.</summary>
        public static RuleExecutionResult Wait(long resumeTick, string resumePortName) => new WaitExecutionResult(resumeTick, resumePortName);

        /// <summary>
        /// Pause the instance waiting for an answer. choiceTexts is exactly 4
        /// entries (one per choice1..choice4 output port); an empty entry means
        /// that choice is unused. timeoutTicks is relative (0 means no limit).
        /// </summary>
        public static RuleExecutionResult Ask(string category, string title, string body, string[] choiceTexts, int defaultChoiceNumber, long timeoutTicks) =>
            new AskExecutionResult(category, title, body, choiceTexts, defaultChoiceNumber, timeoutTicks);
    }

    internal sealed class EndExecutionResult : RuleExecutionResult
    {
    }

    internal sealed class FollowExecutionResult : RuleExecutionResult
    {
        public string PortName { get; }

        public FollowExecutionResult(string portName)
        {
            PortName = portName;
        }
    }

    internal sealed class FollowInOrderExecutionResult : RuleExecutionResult
    {
        public IReadOnlyList<string> PortNames { get; }

        public FollowInOrderExecutionResult(IReadOnlyList<string> portNames)
        {
            PortNames = portNames;
        }
    }

    internal sealed class WaitExecutionResult : RuleExecutionResult
    {
        public long ResumeTick { get; }
        public string ResumePortName { get; }

        public WaitExecutionResult(long resumeTick, string resumePortName)
        {
            ResumeTick = resumeTick;
            ResumePortName = resumePortName;
        }
    }

    internal sealed class AskExecutionResult : RuleExecutionResult
    {
        public string Category { get; }
        public string Title { get; }
        public string Body { get; }

        /// <summary>Exactly 4 entries (choice1..choice4); an empty entry means that choice is unused.</summary>
        public string[] ChoiceTexts { get; }

        public int DefaultChoiceNumber { get; }

        /// <summary>Relative ticks from "now"; 0 means no time limit.</summary>
        public long TimeoutTicks { get; }

        public AskExecutionResult(string category, string title, string body, string[] choiceTexts, int defaultChoiceNumber, long timeoutTicks)
        {
            Category = category;
            Title = title;
            Body = body;
            ChoiceTexts = choiceTexts;
            DefaultChoiceNumber = defaultChoiceNumber;
            TimeoutTicks = timeoutTicks;
        }
    }
}
