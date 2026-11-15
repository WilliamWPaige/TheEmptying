using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A raw, processed or finished good. Immutable once loaded.</summary>
    public sealed class Good : ContentDefinition
    {
        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("category")]
        public GoodCategory Category { get; }

        /// <summary>
        /// In half-shil units: twice the price in shil. 0.5 shil is the
        /// smallest fraction among the starting goods, so half-shil is the
        /// smallest unit the current content needs; every starting price is a
        /// whole number of them.
        /// </summary>
        [JsonProperty("basePriceHalfShil")]
        public int BasePriceHalfShil { get; }

        [JsonConstructor]
        public Good(string id, string name, GoodCategory category, int basePriceHalfShil)
        {
            Id = id;
            Name = name;
            Category = category;
            BasePriceHalfShil = basePriceHalfShil;
        }
    }
}
