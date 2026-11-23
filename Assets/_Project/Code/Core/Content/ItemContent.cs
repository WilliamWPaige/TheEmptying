using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// An item type (R5 bare; R12 adds food value, carry amount, base
    /// value, spoil time and size). "Comes from" is deliberately not a
    /// field here - it's worked out from whichever resource yields this
    /// item.
    /// </summary>
    public sealed class ItemContent : ContentDefinition
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("foodValue")]
        public double FoodValue { get; set; }

        [JsonProperty("carryAmount")]
        public int CarryAmount { get; set; }

        [JsonProperty("baseValueCopper")]
        public int BaseValueCopper { get; set; }

        /// <summary>In-game minutes; 0 means it never spoils.</summary>
        [JsonProperty("spoilTimeMinutes")]
        public double SpoilTimeMinutes { get; set; }

        /// <summary>1 to 10. A stockpile slot holds 100 / Size of this item.</summary>
        [JsonProperty("size")]
        public int Size { get; set; }
    }
}
