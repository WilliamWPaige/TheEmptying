using System.Collections.Generic;
using TheEmptying.Core.Content;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R22: the geometry of a stack spot's four 1x1 m quarters, before the
    /// spot's own rotation is applied. Quarter index 0 is south-west, 1
    /// south-east, 2 north-west, 3 north-east (col = index % 2, row =
    /// index / 2). The four neighbor (edge-adjacent) pairs a Half pile can
    /// use are (0,1), (2,3), (0,2) and (1,3); (0,3) and (1,2) are diagonal
    /// and never valid for a Half pile.
    /// </summary>
    internal static class StockSpotQuarters
    {
        public const int Count = 4;

        private static readonly int[][] HalfPairs =
        {
            new[] { 0, 1 },
            new[] { 2, 3 },
            new[] { 0, 2 },
            new[] { 1, 3 }
        };

        /// <summary>This quarter's center, as an offset from the spot's own center, before the spot's rotation is applied.</summary>
        public static (double LocalX, double LocalZ) LocalOffset(int quarterIndex, double spotSizeMeters)
        {
            double quarterSize = spotSizeMeters * 0.5;
            int col = quarterIndex % 2;
            int row = quarterIndex / 2;
            double localX = (col - 0.5) * quarterSize;
            double localZ = (row - 0.5) * quarterSize;
            return (localX, localZ);
        }

        private static HashSet<int> OccupiedQuarters(StockSpot spot)
        {
            var occupied = new HashSet<int>();
            foreach (StockPile pile in spot.Piles)
            {
                foreach (int quarter in pile.QuarterIndices)
                {
                    occupied.Add(quarter);
                }
            }

            return occupied;
        }

        /// <summary>The quarters a new pile of this shape could use in this spot right now, or null if no free combination fits.</summary>
        public static List<int> FindFreeQuarters(StockSpot spot, PileShape shape)
        {
            HashSet<int> occupied = OccupiedQuarters(spot);

            switch (shape)
            {
                case PileShape.Full:
                    return occupied.Count == 0 ? new List<int> { 0, 1, 2, 3 } : null;

                case PileShape.Half:
                    foreach (int[] pair in HalfPairs)
                    {
                        if (!occupied.Contains(pair[0]) && !occupied.Contains(pair[1]))
                        {
                            return new List<int> { pair[0], pair[1] };
                        }
                    }

                    return null;

                default:
                    for (int quarter = 0; quarter < Count; quarter++)
                    {
                        if (!occupied.Contains(quarter))
                        {
                            return new List<int> { quarter };
                        }
                    }

                    return null;
            }
        }

        /// <summary>How many more piles of this shape this spot's currently-free quarters could still fit, if packed as tightly as possible - used to estimate remaining room, without actually placing anything.</summary>
        public static int MaxAdditionalPiles(StockSpot spot, PileShape shape)
        {
            HashSet<int> occupied = OccupiedQuarters(spot);
            var free = new bool[Count];
            int freeCount = 0;
            for (int quarter = 0; quarter < Count; quarter++)
            {
                free[quarter] = !occupied.Contains(quarter);
                if (free[quarter])
                {
                    freeCount++;
                }
            }

            switch (shape)
            {
                case PileShape.Full:
                    return freeCount == Count ? 1 : 0;

                case PileShape.Half:
                    var used = new bool[Count];
                    int pairs = 0;
                    foreach (int[] pair in HalfPairs)
                    {
                        if (!used[pair[0]] && !used[pair[1]] && free[pair[0]] && free[pair[1]])
                        {
                            used[pair[0]] = true;
                            used[pair[1]] = true;
                            pairs++;
                        }
                    }

                    return pairs;

                default:
                    return freeCount;
            }
        }
    }
}
