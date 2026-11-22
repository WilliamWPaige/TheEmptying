using System.Collections.Generic;
using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A plain, editable list of names (R8) - male first names, female first names, or family names. People are named by picking from these at random.</summary>
    public sealed class NameListDefinition : ContentDefinition
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("names")]
        public List<string> Names { get; set; } = new List<string>();
    }
}
