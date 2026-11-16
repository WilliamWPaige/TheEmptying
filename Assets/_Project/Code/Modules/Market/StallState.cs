using System.Collections.Generic;
using TheEmptying.Core.Entities;

namespace TheEmptying.Modules.Market
{
    /// <summary>One stall at a market building: who rents it (null means the crown) and the goods they've stocked it with.</summary>
    public sealed class StallState
    {
        public EntityId? Renter;
        public Dictionary<string, int> Stock = new Dictionary<string, int>();
    }
}
