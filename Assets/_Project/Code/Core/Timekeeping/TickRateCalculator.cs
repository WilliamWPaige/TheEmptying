using System;

namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// Pure functions only. The actual clock driver (reading real time, calling
    /// SimClock.Tick()) lives in the presentation layer (prompt 1.8).
    /// </summary>
    public static class TickRateCalculator
    {
        public static double TicksPerRealSecond(double realSecondsPerDay, SimSpeed speed)
        {
            if (speed == SimSpeed.Paused || realSecondsPerDay <= 0)
            {
                return 0;
            }

            int multiplier = speed switch
            {
                SimSpeed.Normal => 1,
                SimSpeed.Fast => 2,
                SimSpeed.Fastest => 3,
                _ => 0
            };

            return CalendarConstants.TicksPerDay / realSecondsPerDay * multiplier;
        }

        /// <summary>
        /// How many ticks to run this frame, capped at maxTicksPerFrame so a slow
        /// frame never causes a runaway catch-up. Unspent real time carries over
        /// into leftoverRealSeconds.
        /// </summary>
        public static int TicksToRun(double accumulatedRealSeconds, double ticksPerRealSecond, int maxTicksPerFrame, out double leftoverRealSeconds)
        {
            if (ticksPerRealSecond <= 0 || accumulatedRealSeconds <= 0)
            {
                leftoverRealSeconds = accumulatedRealSeconds;
                return 0;
            }

            double totalTicks = accumulatedRealSeconds * ticksPerRealSecond;
            int ticksToRun = (int)Math.Floor(totalTicks);
            if (ticksToRun > maxTicksPerFrame)
            {
                ticksToRun = maxTicksPerFrame;
            }

            double consumedSeconds = ticksToRun / ticksPerRealSecond;
            leftoverRealSeconds = accumulatedRealSeconds - consumedSeconds;
            return ticksToRun;
        }
    }
}
