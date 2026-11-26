namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R20: one fixed 2x2 m stack spot in a stockpile's layout. Position
    /// and rotation are set once, when the layout is generated at zone
    /// creation, and never change afterward. ItemContentId is null when
    /// the spot is empty; a spot holds exactly one item type at a time,
    /// up to that item's capacity per spot.
    /// </summary>
    public sealed class StockSpot
    {
        public double CenterX { get; set; }
        public double CenterZ { get; set; }
        public double RotationDegrees { get; set; }

        /// <summary>Null means empty.</summary>
        public string ItemContentId { get; set; }

        public int AmountStored { get; set; }

        /// <summary>Which of the item's stack models this spot's stack is using - picked at random when the stack starts (AmountStored rises from 0), kept until the spot empties again. Meaningless while the spot is empty.</summary>
        public int StackModelIndex { get; set; }
    }
}
