using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// R12: a kind of zone the player can draw. For now there are two:
    /// Stockpile and Living Area. ColorHex drives the drawn fill/outline
    /// color, a different one per type.
    /// </summary>
    public sealed class ZoneTypeDefinition : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("colorHex")]
        public string ColorHex { get; set; }
    }
}
