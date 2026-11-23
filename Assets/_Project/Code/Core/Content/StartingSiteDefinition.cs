using Newtonsoft.Json;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// R11: a place a new game can start from. For now there is exactly
    /// one, at the map center; later there are 3 or 4 predetermined
    /// starting areas. Position is a fraction of the world (0..1), not
    /// literal meters, so "the map center" stays correct even if the
    /// world's size setting ever changes.
    /// </summary>
    public sealed class StartingSiteDefinition : ContentDefinition
    {
        [JsonProperty("fractionX")]
        public double FractionX { get; set; }

        [JsonProperty("fractionZ")]
        public double FractionZ { get; set; }
    }
}
