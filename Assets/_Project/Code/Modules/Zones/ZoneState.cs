using System.Collections.Generic;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R12: one placed zone - its type and rectangle, plus (Stockpile
    /// only) per-item stored amounts, keep-on-hand limits and blocks. A
    /// Living Area has no entries here.
    /// </summary>
    public sealed class ZoneState
    {
        public string ZoneTypeId { get; set; }
        public double CenterX { get; set; }
        public double CenterZ { get; set; }
        public double SideALength { get; set; }
        public double SideBLength { get; set; }
        public double RotationDegrees { get; set; }

        /// <summary>Stockpile only, keyed by item content Id. An item with no entry here has never been stored, limited or blocked - it's still "known to" the stockpile, just with every default value.</summary>
        public Dictionary<string, StockpileItemEntry> StockpileItems { get; } = new Dictionary<string, StockpileItemEntry>();

        public OrientedRect Rect => new OrientedRect(CenterX, CenterZ, SideALength, SideBLength, RotationDegrees);
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
