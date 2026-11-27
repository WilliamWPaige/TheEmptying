using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R20: one fixed 2x2 m stack spot in a stockpile's layout. Position
    /// and rotation are set once, when the layout is generated at zone
    /// creation, and never change afterward. R22: a spot splits into four
    /// 1x1 m quarters (StockSpotQuarters) and holds a list of piles, each
    /// covering one or more quarters - a spot with an empty Piles list is
    /// completely free.
    /// </summary>
    public sealed class StockSpot
    {
        public double CenterX { get; set; }
        public double CenterZ { get; set; }
        public double RotationDegrees { get; set; }

        public List<StockPile> Piles { get; } = new List<StockPile>();

        /// <summary>The sum of every pile's AmountStored in this spot - for display and occupancy checks only, never stored.</summary>
        public int TotalStored => Piles.Sum(p => p.AmountStored);
    }
}
