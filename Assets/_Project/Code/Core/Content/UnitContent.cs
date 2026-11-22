using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A unit type. Bare by design (R5); later systems add their own fields.</summary>
    public sealed class UnitContent : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }
}
