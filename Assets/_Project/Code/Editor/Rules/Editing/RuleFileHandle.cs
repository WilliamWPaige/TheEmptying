using System.IO;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>One rule content file (a JSON file holding a "rule" items array) inside a source's Data folder.</summary>
    public sealed class RuleFileHandle
    {
        public RuleContentSource Source { get; }
        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);

        public RuleFileHandle(RuleContentSource source, string filePath)
        {
            Source = source;
            FilePath = filePath;
        }
    }
}
