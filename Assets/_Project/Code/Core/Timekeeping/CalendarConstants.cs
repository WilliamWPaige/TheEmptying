namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// Fixed for now; these become settings later (see Phase 5 Technical Framework, Milestone 1).
    /// </summary>
    public static class CalendarConstants
    {
        public const int TicksPerMinute = 10;
        public const int MinutesPerHour = 60;
        public const int HoursPerDay = 24;
        public const int DaysPerSeason = 15;
        public const int SeasonsPerYear = 4;

        public const long TicksPerHour = TicksPerMinute * MinutesPerHour;
        public const long TicksPerDay = TicksPerHour * HoursPerDay;
        public const long TicksPerSeason = TicksPerDay * DaysPerSeason;
        public const long TicksPerYear = TicksPerSeason * SeasonsPerYear;
    }
}
