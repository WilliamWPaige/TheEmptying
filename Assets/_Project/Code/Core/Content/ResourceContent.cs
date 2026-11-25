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

        /// <summary>R15: "gather nearby" - standing on a free ground cell within GatherRadiusMeters of this resource (never on the resource itself) gathers this item, instead of harvesting the resource directly. Empty when this resource has no gather-nearby behavior.</summary>
        [JsonProperty("gatherItem")]
        public ContentRef<ItemContent> GatherItem { get; set; }

        [JsonProperty("gatherRadiusMeters")]
        public double GatherRadiusMeters { get; set; }

        /// <summary>In-game minutes.</summary>
        [JsonProperty("gatherTimeUnskilledMinutes")]
        public double GatherTimeUnskilledMinutes { get; set; }

        [JsonProperty("gatherTimeSkilledMinutes")]
        public double GatherTimeSkilledMinutes { get; set; }

        [JsonProperty("gatherTimeMasterMinutes")]
        public double GatherTimeMasterMinutes { get; set; }

        /// <summary>R15: how many people may gather near this resource at once - the same crowding rule MaxWorkers applies to direct harvesting.</summary>
        [JsonProperty("gatherMaxConcurrent")]
        public int GatherMaxConcurrent { get; set; } = 1;

        /// <summary>R15: an optional extra item (e.g. flint while gathering rock) also given by chance (GatherBonusChance) each time GatherItem is gathered; empty means no bonus.</summary>
        [JsonProperty("gatherBonusItem")]
        public ContentRef<ItemContent> GatherBonusItem { get; set; }

        [JsonProperty("gatherBonusChance")]
        public double GatherBonusChance { get; set; }
    }
}
