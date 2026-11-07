namespace TheEmptying.Core.Content
{
    /// <summary>Recorded when a later-loaded definition with the same type and Id replaces an earlier one.</summary>
    public sealed class ContentOverride
    {
        public string TypeName { get; }
        public string Id { get; }
        public string PreviousSourceId { get; }
        public string NewSourceId { get; }

        public ContentOverride(string typeName, string id, string previousSourceId, string newSourceId)
        {
            TypeName = typeName;
            Id = id;
            PreviousSourceId = previousSourceId;
            NewSourceId = newSourceId;
        }

        public override string ToString()
        {
            return $"{TypeName}.{Id}: '{NewSourceId}' overrides '{PreviousSourceId}'";
        }
    }
}
