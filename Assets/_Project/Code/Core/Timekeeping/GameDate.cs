namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// Derived from a tick count. Tick 0 is Year 1, Spring, day 1, 00:00.
    /// </summary>
    public readonly struct GameDate
    {
        public int Year { get; }
        public Season Season { get; }
        public int DayOfSeason { get; }
        public int Hour { get; }
        public int Minute { get; }

        public GameDate(int year, Season season, int dayOfSeason, int hour, int minute)
        {
            Year = year;
            Season = season;
            DayOfSeason = dayOfSeason;
            Hour = hour;
            Minute = minute;
        }

        public static GameDate FromTick(long tick)
        {
            long totalMinutes = tick / CalendarConstants.TicksPerMinute;
            int minute = (int)(totalMinutes % CalendarConstants.MinutesPerHour);

            long totalHours = totalMinutes / CalendarConstants.MinutesPerHour;
            int hour = (int)(totalHours % CalendarConstants.HoursPerDay);

            long totalDays = totalHours / CalendarConstants.HoursPerDay;
            int dayOfSeason = (int)(totalDays % CalendarConstants.DaysPerSeason) + 1;

            long totalSeasons = totalDays / CalendarConstants.DaysPerSeason;
            int seasonIndex = (int)(totalSeasons % CalendarConstants.SeasonsPerYear);
            int year = (int)(totalSeasons / CalendarConstants.SeasonsPerYear) + 1;

            return new GameDate(year, (Season)seasonIndex, dayOfSeason, hour, minute);
        }
    }
}
