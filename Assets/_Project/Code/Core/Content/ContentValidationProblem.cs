namespace TheEmptying.Core.Content
{
    /// <summary>One reported problem: a missing Id, a bad reference, an out-of-range value, an unknown type, a malformed file.</summary>
    public sealed class ContentValidationProblem
    {
        public string SourceId { get; }
        public string File { get; }
        public string ItemId { get; }
        public string Field { get; }
        public string Message { get; }

        public ContentValidationProblem(string sourceId, string file, string itemId, string field, string message)
        {
            SourceId = sourceId;
            File = file;
            ItemId = itemId;
            Field = field;
            Message = message;
        }

        public override string ToString()
        {
            return $"[{SourceId}/{File}] {ItemId ?? "?"}.{Field ?? "?"}: {Message}";
        }
    }
}
