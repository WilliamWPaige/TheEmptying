using System.Collections.Generic;

namespace TheEmptying.Core.Content
{
    public sealed class ContentLoadReport
    {
        public List<ContentValidationProblem> Problems { get; } = new List<ContentValidationProblem>();
        public List<ContentOverride> Overrides { get; } = new List<ContentOverride>();

        public bool HasErrors => Problems.Count > 0;
    }
}
