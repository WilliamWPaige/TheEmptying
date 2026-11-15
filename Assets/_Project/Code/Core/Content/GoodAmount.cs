using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>A quantity of a good: a construction cost line, or a recipe input or output.</summary>
    public readonly struct GoodAmount
    {
        [JsonProperty("good")]
        public ContentRef<Good> Good { get; }

        [JsonProperty("amount")]
        public int Amount { get; }

        [JsonConstructor]
        public GoodAmount(ContentRef<Good> good, int amount)
        {
            Good = good;
            Amount = amount;
        }
    }
}
