using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// A reference to another content item of type T, stored as its Id. Resolved
    /// (checked against the database, and fetched on demand) in the validation
    /// pass after loading, not inline during deserialization.
    /// </summary>
    public readonly struct ContentRef<T> where T : ContentDefinition
    {
        [JsonProperty("id")]
        public string Id { get; }

        [JsonConstructor]
        public ContentRef(string id)
        {
            Id = id;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Id);
    }
}
