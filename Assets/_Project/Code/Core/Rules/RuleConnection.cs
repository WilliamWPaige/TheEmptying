using Newtonsoft.Json;

namespace TheEmptying.Core.Rules
{
    /// <summary>A wire from one node's output port to another node's input port.</summary>
    public sealed class RuleConnection
    {
        [JsonProperty("fromNode")]
        public string FromNode { get; set; }

        [JsonProperty("fromPort")]
        public string FromPort { get; set; }

        [JsonProperty("toNode")]
        public string ToNode { get; set; }

        [JsonProperty("toPort")]
        public string ToPort { get; set; }
    }
}
