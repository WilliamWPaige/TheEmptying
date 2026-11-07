using System.Collections.Generic;

namespace TheEmptying.Core.Content
{
    /// <summary>Passed to a content type's validator for one item.</summary>
    public sealed class ContentValidationContext
    {
        public ContentDatabase Database { get; }
        public string SourceId { get; }
        public string File { get; }

        private readonly List<ContentValidationProblem> _problems;

        internal ContentValidationContext(ContentDatabase database, string sourceId, string file, List<ContentValidationProblem> problems)
        {
            Database = database;
            SourceId = sourceId;
            File = file;
            _problems = problems;
        }

        public void ReportProblem(string itemId, string field, string message)
        {
            _problems.Add(new ContentValidationProblem(SourceId, File, itemId, field, message));
        }
    }
}
