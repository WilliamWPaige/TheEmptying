using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>Content, so mods can add settings. Becomes a slider in the custom difficulty panel.</summary>
    public sealed class SettingDefinition : ContentDefinition
    {
        [JsonProperty("valueType")]
        public SettingValueType ValueType { get; set; }

        [JsonProperty("default")]
        public double Default { get; set; }

        [JsonProperty("minimum")]
        public double Minimum { get; set; }

        [JsonProperty("maximum")]
        public double Maximum { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>R9: the short, readable name shown in the Cheats tab instead of the raw Id (which is still shown, with the description, on hover). Required.</summary>
        [JsonProperty("label")]
        public string Label { get; set; }
    }
}
