using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Startup
{
    /// <summary>The combined report from one SimulationBuilder.Build call: disabled modules, content problems, and content overrides.</summary>
    public sealed class StartupReport
    {
        public IReadOnlyList<string> DisabledModuleReasons { get; }
        public IReadOnlyList<ContentValidationProblem> ContentProblems { get; }
        public IReadOnlyList<ContentOverride> Overrides { get; }

        internal StartupReport(IReadOnlyList<string> disabledModuleReasons, ContentLoadReport contentReport)
        {
            DisabledModuleReasons = disabledModuleReasons;
            ContentProblems = contentReport.Problems;
            Overrides = contentReport.Overrides;
        }

        public override string ToString()
        {
            var lines = new List<string>();
            lines.AddRange(DisabledModuleReasons);
            lines.AddRange(ContentProblems.Select(p => p.ToString()));
            lines.AddRange(Overrides.Select(o => o.ToString()));

            return lines.Count == 0
                ? "Startup clean: no disabled modules, no content problems, no overrides."
                : string.Join("\n", lines);
        }
    }
}
