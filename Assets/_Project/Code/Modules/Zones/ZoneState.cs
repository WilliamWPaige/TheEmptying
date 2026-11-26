using System.Collections.Generic;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R18b: one placed zone - its type, player-given name, color (as a
    /// hex string; the simulation never touches UnityEngine.Color) and
    /// the set of cells it covers, which may form several separate
    /// pieces. CenterX/CenterZ are a cached centroid (in world meters,
    /// computed once by ZonesModule from Cells) for "nearest zone"
    /// distance heuristics only - never the authoritative shape, and
    /// never recomputed here. A Living Area has no Spots or
    /// StockpileItems entries.
    /// </summary>
    public sealed class ZoneState
    {
        public string ZoneTypeId { get; set; }
        public string Name { get; set; }
        public string ColorHex { get; set; }
        public double CenterX { get; set; }
        public double CenterZ { get; set; }

        public HashSet<CellCoord> Cells { get; } = new HashSet<CellCoord>();

        /// <summary>Stockpile only: which layout (ZonesModule.LayoutUniformId or LayoutNaturalId) its Spots were generated with.</summary>
        public string LayoutId { get; set; }

        /// <summary>R20: stockpile only - this zone's fixed stack spots, laid out once (from the world seed) when the zone is created, and never moved or added to afterward.</summary>
        public List<StockSpot> Spots { get; } = new List<StockSpot>();

        /// <summary>Stockpile only, keyed by item content Id. An item with no entry here has never been limited or blocked - it's still "known to" the stockpile, just with every default value. Amount stored is the live sum of that item's Spots, not kept here - see ZonesModule.</summary>
        public Dictionary<string, StockpileItemEntry> StockpileItems { get; } = new Dictionary<string, StockpileItemEntry>();
    }

    /// <summary>One item type's limit/block state at one stockpile - amount stored is derived from Spots, not stored here.</summary>
    public sealed class StockpileItemEntry
    {
        /// <summary>Null means "use the default" (the most this stockpile could hold of this item alone) - see ZonesModule.GetStockpileItemInfo.</summary>
        public int? KeepOnHandLimitOverride { get; set; }

        public bool Blocked { get; set; }
    }
}
