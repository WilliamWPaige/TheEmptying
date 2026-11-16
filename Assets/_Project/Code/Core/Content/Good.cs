using Newtonsoft.Json;
using TheEmptying.Core.Economy;

namespace TheEmptying.Core.Content
{
    /// <summary>A raw, processed or finished good. Immutable once loaded.</summary>
    public sealed class Good : ContentDefinition
    {
        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("category")]
        public GoodCategory Category { get; }

        [JsonProperty("basePrice")]
        public Money BasePrice { get; }

        [JsonConstructor]
        public Good(string id, string name, GoodCategory category, Money basePrice)
        {
            Id = id;
            Name = name;
            Category = category;
            BasePrice = basePrice;
        }
    }
}
