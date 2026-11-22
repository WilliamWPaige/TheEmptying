using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// One skill (R8): a stable Id, a display name, the group it's shown
    /// under in the info window, and its position in the doc's listed
    /// order (ListByType sorts by Id, not file order, so this is how the
    /// authored order survives).
    /// </summary>
    public sealed class SkillDefinition : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("group")]
        public string Group { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }
    }
}
