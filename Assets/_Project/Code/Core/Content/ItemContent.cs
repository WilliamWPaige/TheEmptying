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

        /// <summary>R15: tenths of a copper, so placeholder fractional prices (0.2 copper for a stick or rock) stay an exact integer; 10 means 1 whole copper.</summary>
        [JsonProperty("baseValueTenthCopper")]
        public int BaseValueTenthCopper { get; set; }

        /// <summary>In-game minutes; 0 means it never spoils.</summary>
        [JsonProperty("spoilTimeMinutes")]
        public double SpoilTimeMinutes { get; set; }

        /// <summary>1 to 10. A stockpile spot holds 100 / Size of this item.</summary>
        [JsonProperty("size")]
        public int Size { get; set; }

        /// <summary>R20: how many stack models this item has (its box's visuals are Presentation-only; the simulation only needs the count, to pick a model index deterministically). 0 means there's nothing to pick - the spot it starts a stack in just uses index 0.</summary>
        [JsonProperty("stackModelCount")]
        public int StackModelCount { get; set; }

        /// <summary>R22: how much of a stockpile spot this item's piles cover - Quarter, Half or Full.</summary>
        [JsonProperty("pileShape")]
        public PileShape PileShape { get; set; }
    }
}
