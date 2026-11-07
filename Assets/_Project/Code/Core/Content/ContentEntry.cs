namespace TheEmptying.Core.Content
{
    public sealed class ContentEntry
    {
        public ContentDefinition Definition { get; }
        public string SourceId { get; }
        public string SourceFile { get; }

        public ContentEntry(ContentDefinition definition, string sourceId, string sourceFile)
        {
            Definition = definition;
            SourceId = sourceId;
            SourceFile = sourceFile;
        }
    }
}
