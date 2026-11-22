using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>One attribute (R8): a stable Id, a display name, and its position in the doc's listed order.</summary>
    public sealed class AttributeDefinition : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }
    }
}
