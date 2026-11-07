using NUnit.Framework;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Timekeeping
{
    public class SimClockTests
    {
        [Test]
        public void Tick_MidHour_CrossesNoBoundaries()
        {
            var clock = new SimClock();
            TickBoundaries boundaries = clock.Tick();

            Assert.IsFalse(boundaries.NewHour);
            Assert.IsFalse(boundaries.NewDay);
            Assert.IsFalse(boundaries.NewSeason);
            Assert.IsFalse(boundaries.NewYear);
        }

        [Test]
        public void Tick_ReportsHourBoundaryOnly()
        {
            var clock = new SimClock();
            TickBoundaries last = default;
            for (long i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                last = clock.Tick();
            }

            Assert.IsTrue(last.NewHour);
            Assert.IsFalse(last.NewDay);
            Assert.IsFalse(last.NewSeason);
            Assert.IsFalse(last.NewYear);
        }

        [Test]
        public void Tick_ReportsDayBoundary()
        {
            var clock = new SimClock();
            TickBoundaries last = default;
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                last = clock.Tick();
            }

            Assert.IsTrue(last.NewHour);
            Assert.IsTrue(last.NewDay);
            Assert.IsFalse(last.NewSeason);
            Assert.IsFalse(last.NewYear);
        }

        [Test]
        public void Tick_ReportsSeasonBoundary()
        {
            var clock = new SimClock();
            TickBoundaries last = default;
            for (long i = 0; i < CalendarConstants.TicksPerSeason; i++)
            {
                last = clock.Tick();
            }

            Assert.IsTrue(last.NewDay);
            Assert.IsTrue(last.NewSeason);
            Assert.IsFalse(last.NewYear);
        }

        [Test]
        public void Tick_ReportsYearBoundary()
        {
            var clock = new SimClock();
            TickBoundaries last = default;
            for (long i = 0; i < CalendarConstants.TicksPerYear; i++)
            {
                last = clock.Tick();
            }

            Assert.IsTrue(last.NewSeason);
            Assert.IsTrue(last.NewYear);
        }
    }
}
