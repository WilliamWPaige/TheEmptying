namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// Derived from a tick count. Tick 0 is Year 1, Month 1 (Waking), day 1, 00:00.
    /// </summary>
    public readonly struct GameDate
    {
        public int Year { get; }
        public int Month { get; }
        public Season Season { get; }
        public int DayOfMonth { get; }
        public int Hour { get; }
        public int Minute { get; }

        public GameDate(int year, int month, Season season, int dayOfMonth, int hour, int minute)
        {
            Year = year;
            Month = month;
            Season = season;
            DayOfMonth = dayOfMonth;
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
            int dayOfMonth = (int)(totalDays % CalendarConstants.DaysPerMonth) + 1;

            long totalMonths = totalDays / CalendarConstants.DaysPerMonth;
            int monthIndex = (int)(totalMonths % CalendarConstants.MonthsPerYear);
            int year = (int)(totalMonths / CalendarConstants.MonthsPerYear) + 1;

            var season = (Season)(monthIndex / CalendarConstants.MonthsPerSeason);
            int month = monthIndex + 1;

            return new GameDate(year, month, season, dayOfMonth, hour, minute);
        }

        public override string ToString()
        {
            if (Season == Season.Hollow)
            {
                return $"Day {DayOfMonth}, the Hollow, Year {Year}";
            }

            return $"Day {DayOfMonth}, {Season} (month {Month}), Year {Year}";
        }

        /// <summary>3.9i: the clock's own time-of-day string - 12-hour with AM/PM by default (e.g. "7:30 AM", "12:00 PM"), or 24-hour (setting.ui.clock_24h) when use24Hour is true.</summary>
        public string ToClockString(bool use24Hour)
        {
            if (use24Hour)
            {
                return $"{Hour:D2}:{Minute:D2}";
            }

            int hour12 = Hour % 12;
            if (hour12 == 0)
            {
                hour12 = 12;
            }

            string suffix = Hour < 12 ? "AM" : "PM";
            return $"{hour12}:{Minute:D2} {suffix}";
        }
    }
}
