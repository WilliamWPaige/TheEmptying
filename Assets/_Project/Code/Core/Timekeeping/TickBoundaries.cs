namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// Which calendar boundaries a single Tick() call crossed. No event system yet
    /// (prompt 1.4); whoever drives the clock decides what to do with this.
    /// </summary>
    public readonly struct TickBoundaries
    {
        public bool NewHour { get; }
        public bool NewDay { get; }
        public bool NewSeason { get; }
        public bool NewYear { get; }

        public TickBoundaries(bool newHour, bool newDay, bool newSeason, bool newYear)
        {
            NewHour = newHour;
            NewDay = newDay;
            NewSeason = newSeason;
            NewYear = newYear;
        }
    }
}
