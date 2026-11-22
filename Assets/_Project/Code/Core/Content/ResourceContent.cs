using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A resource node type: has a footprint, in cells. Bare by design (R5); later systems add their own fields.</summary>
    public sealed class ResourceContent : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("footprintWidth")]
        public int FootprintWidth { get; set; }

        [JsonProperty("footprintDepth")]
        public int FootprintDepth { get; set; }
    }
}
