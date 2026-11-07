namespace TheEmptying.Core.Scheduling
{
    /// <summary>
    /// Starting values from the Phase 5 Technical Framework. These become settings later.
    /// </summary>
    public static class ImportanceTierConstants
    {
        public const int HighMultiplier = 1;
        public const int NormalMultiplier = 4;
        public const int LowMultiplier = 16;

        public static int GetMultiplier(ImportanceTier tier)
        {
            switch (tier)
            {
                case ImportanceTier.High:
                    return HighMultiplier;
                case ImportanceTier.Low:
                    return LowMultiplier;
                default:
                    return NormalMultiplier;
            }
        }
    }
}
