using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    public sealed class RuleCompileReport
    {
        public List<RuleCompileProblem> Errors { get; } = new List<RuleCompileProblem>();
        public List<RuleCompileProblem> Warnings { get; } = new List<RuleCompileProblem>();

        public bool HasErrors => Errors.Count > 0;
    }
}
