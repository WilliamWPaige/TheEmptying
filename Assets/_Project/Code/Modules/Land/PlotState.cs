using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;

namespace TheEmptying.Modules.Land
{
    /// <summary>
    /// One plot: a fixed rectangle inside one zone. Either crown-owned (both
    /// null), bought (Owner set) or leased (Leaseholder set) - never both at
    /// once.
    /// </summary>
    public sealed class PlotState
    {
        public string ZoneId;
        public CellRect Rect;
        public EntityId? Owner;
        public EntityId? Leaseholder;

        /// <summary>Unpaid land tax, accumulated month over month. Informational only for now; consequences come with laws later.</summary>
        public Money TaxDebt = Money.Zero;
    }
}
