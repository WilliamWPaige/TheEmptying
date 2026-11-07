namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// The simulation's only source of time. Advances one tick at a time; never
    /// touches frame rate or the system clock.
    /// </summary>
    public sealed class SimClock
    {
        public long CurrentTick { get; private set; }

        public SimClock(long startingTick = 0)
        {
            CurrentTick = startingTick;
        }

        public GameDate CurrentDate => GameDate.FromTick(CurrentTick);

        public TickBoundaries Tick()
        {
            CurrentTick++;

            bool newHour = CurrentTick % CalendarConstants.TicksPerHour == 0;
            bool newDay = CurrentTick % CalendarConstants.TicksPerDay == 0;
            bool newSeason = CurrentTick % CalendarConstants.TicksPerSeason == 0;
            bool newYear = CurrentTick % CalendarConstants.TicksPerYear == 0;

            return new TickBoundaries(newHour, newDay, newSeason, newYear);
        }
    }
}
