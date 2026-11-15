namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// Fixed for now; these become settings later (see Phase 5 Technical Framework, Milestone 1).
    /// Calendar replaced in prompt 3.1: 28-day months, 13 months a year (364 days). Seasons
    /// are 3 months each (Waking, Bloom, Reaping, Frost) plus the Hollow, the 13th month,
    /// which stands alone as its own season.
    /// </summary>
    public static class CalendarConstants
    {
        public const int TicksPerMinute = 10;
        public const int MinutesPerHour = 60;
        public const int HoursPerDay = 24;
        public const int DaysPerMonth = 28;
        public const int MonthsPerYear = 13;
        public const int MonthsPerSeason = 3;

        public const long TicksPerHour = TicksPerMinute * MinutesPerHour;
        public const long TicksPerDay = TicksPerHour * HoursPerDay;
        public const long TicksPerMonth = TicksPerDay * DaysPerMonth;
        public const long TicksPerYear = TicksPerMonth * MonthsPerYear;
    }
}
