namespace TheEmptying.Core.Content
{
    /// <summary>R22: how much of a stockpile spot's four 1x1 m quarters one pile of this item covers - Quarter (1), Half (2 neighboring quarters) or Full (all 4). Nothing is ever bigger than a full spot.</summary>
    public enum PileShape
    {
        Quarter,
        Half,
        Full
    }

    public static class PileShapeExtensions
    {
        /// <summary>How many of a spot's four quarters one pile of this shape covers.</summary>
        public static int QuarterCount(this PileShape shape)
        {
            switch (shape)
            {
                case PileShape.Half:
                    return 2;
                case PileShape.Full:
                    return 4;
                default:
                    return 1;
            }
        }
    }
}
