using System.Collections.Generic;

namespace TheEmptying.Modules.Market
{
    /// <summary>A market building's fixed stalls (placeholder: 12 per market, created the first time the building is seen).</summary>
    public sealed class MarketBuildingState
    {
        public List<StallState> Stalls = new List<StallState>();
    }
}
