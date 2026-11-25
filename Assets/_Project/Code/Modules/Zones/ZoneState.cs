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
    /// never recomputed here. A Living Area has no StockpileItems entries.
    /// </summary>
    public sealed class ZoneState
    {
        public string ZoneTypeId { get; set; }
        public string Name { get; set; }
        public string ColorHex { get; set; }
        public double CenterX { get; set; }
        public double CenterZ { get; set; }

        public HashSet<CellCoord> Cells { get; } = new HashSet<CellCoord>();

        /// <summary>Stockpile only, keyed by item content Id. An item with no entry here has never been stored, limited or blocked - it's still "known to" the stockpile, just with every default value.</summary>
        public Dictionary<string, StockpileItemEntry> StockpileItems { get; } = new Dictionary<string, StockpileItemEntry>();
    }

    /// <summary>One item type's state at one stockpile.</summary>
    public sealed class StockpileItemEntry
    {
        public int AmountStored { get; set; }

        /// <summary>Null means "use the default" (the most this stockpile could hold of this item alone) - see ZonesModule.GetStockpileItemInfo.</summary>
        public int? KeepOnHandLimitOverride { get; set; }

        public bool Blocked { get; set; }
    }
}
