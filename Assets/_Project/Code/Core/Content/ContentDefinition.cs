using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>Base for every content type. A permanent text Id, readable such as "resource.iron_ore".</summary>
    public abstract class ContentDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
