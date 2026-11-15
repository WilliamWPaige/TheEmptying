using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A land zone (residential, commercial, industrial, farm...). Immutable once loaded.</summary>
    public sealed class Zone : ContentDefinition
    {
        [JsonProperty("name")]
        public string Name { get; }

        /// <summary>The class a residential zone houses; null for a non-residential zone.</summary>
        [JsonProperty("classTier")]
        public SocialClass? ClassTier { get; }

        [JsonConstructor]
        public Zone(string id, string name, SocialClass? classTier)
        {
            Id = id;
            Name = name;
            ClassTier = classTier;
        }
    }
}
