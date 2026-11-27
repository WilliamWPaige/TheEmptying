using System.Collections.Generic;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R22: one pile of a single item inside a stack spot, covering one or
    /// more of the spot's four quarters (see StockSpotQuarters) depending
    /// on the item's PileShape. A spot can hold several piles, of the same
    /// or different items, as long as each pile's quarters don't overlap
    /// another pile's. A pile is removed from its spot's Piles list the
    /// moment it empties, freeing its quarters for the next pile.
    /// </summary>
    public sealed class StockPile
    {
        public string ItemContentId { get; set; }
        public int AmountStored { get; set; }

        /// <summary>Which of the item's stack models this pile is using - picked at random when the pile starts, kept for its lifetime.</summary>
        public int StackModelIndex { get; set; }

        /// <summary>The quarter indices (0-3, see StockSpotQuarters) this pile occupies - one for Quarter, two neighboring for Half, all four for Full.</summary>
        public List<int> QuarterIndices { get; } = new List<int>();
    }
}
