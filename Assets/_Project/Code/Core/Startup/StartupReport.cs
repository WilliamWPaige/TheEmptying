using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Rules;

namespace TheEmptying.Core.Startup
{
    /// <summary>The combined report from one SimulationBuilder.Build call: disabled modules, content problems, content overrides, and rule compile errors and warnings.</summary>
    public sealed class StartupReport
    {
        public IReadOnlyList<string> DisabledModuleReasons { get; }
        public IReadOnlyList<ContentValidationProblem> ContentProblems { get; }
        public IReadOnlyList<ContentOverride> Overrides { get; }
        public IReadOnlyList<RuleCompileProblem> RuleErrors { get; }
        public IReadOnlyList<RuleCompileProblem> RuleWarnings { get; }

        internal StartupReport(IReadOnlyList<string> disabledModuleReasons, ContentLoadReport contentReport, RuleCompileReport ruleReport)
        {
            DisabledModuleReasons = disabledModuleReasons;
            ContentProblems = contentReport.Problems;
            Overrides = contentReport.Overrides;
            RuleErrors = ruleReport.Errors;
            RuleWarnings = ruleReport.Warnings;
        }

        public override string ToString()
        {
            var lines = new List<string>();
            lines.AddRange(DisabledModuleReasons);
            lines.AddRange(ContentProblems.Select(p => p.ToString()));
            lines.AddRange(Overrides.Select(o => o.ToString()));
            lines.AddRange(RuleErrors.Select(e => e.ToString()));
            lines.AddRange(RuleWarnings.Select(w => w.ToString()));

            return lines.Count == 0
                ? "Startup clean: no disabled modules, no content problems, no overrides, no rule problems."
                : string.Join("\n", lines);
        }
    }
}
