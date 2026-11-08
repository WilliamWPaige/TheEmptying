using System.Collections.Generic;
using Newtonsoft.Json;
using TheEmptying.Core.Content;

namespace TheEmptying.Core.Rules
{
    /// <summary>Content: a rule's graph of nodes and connections. Loaded from a source's Data folder like any other content; mods add and override by Id.</summary>
    public sealed class RuleDefinition : ContentDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("nodes")]
        public List<RuleNode> Nodes { get; set; } = new List<RuleNode>();

        [JsonProperty("connections")]
        public List<RuleConnection> Connections { get; set; } = new List<RuleConnection>();
    }
}
