using System.Collections.Generic;
using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A source folder's own mod.json: Id, Name, Version, Dependencies.</summary>
    public sealed class SourceManifest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; } = new List<string>();
    }
}
