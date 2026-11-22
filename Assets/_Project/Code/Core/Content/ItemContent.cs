using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>An item type. Bare by design (R5); later systems add their own fields.</summary>
    public sealed class ItemContent : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }
}
