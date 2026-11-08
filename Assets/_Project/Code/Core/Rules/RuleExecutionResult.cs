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
}
