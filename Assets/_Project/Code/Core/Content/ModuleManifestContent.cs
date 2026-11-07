using System.Collections.Generic;
using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>Data-file form of a module manifest. "A module with no manifest in the data is an error."</summary>
    public sealed class ModuleManifestContent : ContentDefinition
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; } = new List<string>();

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;
    }
}
