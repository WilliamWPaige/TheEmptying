namespace TheEmptying.Core.Content
{
    /// <summary>
    /// One content item already converted from its own authoring source (a
    /// ScriptableObject, in the shipped game) into a definition for
    /// ContentLoader to add to the database alongside its JSON sources.
    /// </summary>
    public sealed class ContentAssetDefinition
    {
        public string TypeName { get; }
        public ContentDefinition Definition { get; }
        public string SourceId { get; }
        public string SourceFile { get; }

        public ContentAssetDefinition(string typeName, ContentDefinition definition, string sourceId, string sourceFile)
        {
            TypeName = typeName;
            Definition = definition;
            SourceId = sourceId;
            SourceFile = sourceFile;
        }
    }
}
