using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// A resource node type (R5 bare; R12 adds what it yields, how much
    /// it holds, and Unskilled/Skilled/Master harvest times).
    /// </summary>
    public sealed class ResourceContent : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("footprintWidth")]
        public int FootprintWidth { get; set; }

        [JsonProperty("footprintDepth")]
        public int FootprintDepth { get; set; }

        [JsonProperty("yieldedItem")]
        public ContentRef<ItemContent> YieldedItem { get; set; }

        [JsonProperty("unlimitedAmount")]
        public bool UnlimitedAmount { get; set; }

        /// <summary>Ignored when UnlimitedAmount is true.</summary>
        [JsonProperty("amountHeld")]
        public int AmountHeld { get; set; }

        /// <summary>In-game minutes.</summary>
        [JsonProperty("harvestTimeUnskilledMinutes")]
        public double HarvestTimeUnskilledMinutes { get; set; }

        [JsonProperty("harvestTimeSkilledMinutes")]
        public double HarvestTimeSkilledMinutes { get; set; }

        [JsonProperty("harvestTimeMasterMinutes")]
        public double HarvestTimeMasterMinutes { get; set; }

        /// <summary>R13b: how many people may work this resource at once (dig at a grub mound, say). A resource at its limit is treated as unavailable.</summary>
        [JsonProperty("maxWorkers")]
        public int MaxWorkers { get; set; } = 1;
    }
}
