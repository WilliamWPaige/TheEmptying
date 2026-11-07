using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>Data-file form of an event definition. Loaded content registers into the EventDefinitionRegistry.</summary>
    public sealed class EventDefinitionContent : ContentDefinition
    {
        [JsonProperty("visibility")]
        public string Visibility { get; set; }

        [JsonProperty("reportingScore")]
        public float ReportingScore { get; set; }

        [JsonProperty("notabilityScore")]
        public float NotabilityScore { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
