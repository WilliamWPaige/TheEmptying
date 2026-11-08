namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>One content source the rule editor can browse and save into: the core folder, or a mod folder.</summary>
    public sealed class RuleContentSource
    {
        public string Name { get; }
        public string FolderPath { get; }
        public string DataFolderPath { get; }

        public RuleContentSource(string name, string folderPath)
        {
            Name = name;
            FolderPath = folderPath;
            DataFolderPath = System.IO.Path.Combine(folderPath, "Data");
        }
    }
}
