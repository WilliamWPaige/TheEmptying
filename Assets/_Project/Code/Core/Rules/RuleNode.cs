using System.Collections.Generic;
using Newtonsoft.Json;

namespace TheEmptying.Core.Rules
{
    /// <summary>One node in a rule: a block type instance, its editor position, and its field values.</summary>
    public sealed class RuleNode
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("blockType")]
        public string BlockTypeId { get; set; }

        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("fields")]
        public Dictionary<string, RuleFieldValueSource> Fields { get; set; } = new Dictionary<string, RuleFieldValueSource>();
    }
}
