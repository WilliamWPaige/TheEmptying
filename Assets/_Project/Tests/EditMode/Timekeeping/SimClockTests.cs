using System.Collections.Generic;
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
            Assert.IsFalse(boundaries.NewMonth);
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
            Assert.IsFalse(last.NewMonth);
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
            Assert.IsFalse(last.NewMonth);
            Assert.IsFalse(last.NewSeason);
            Assert.IsFalse(last.NewYear);
        }

        [Test]
        public void Tick_ReportsMonthBoundary_ButNotYetASeasonBoundary()
        {
            var clock = new SimClock();
            TickBoundaries last = default;
            for (long i = 0; i < CalendarConstants.TicksPerMonth; i++)
            {
                last = clock.Tick();
            }

            Assert.IsTrue(last.NewDay);
            Assert.IsTrue(last.NewMonth);
            Assert.IsFalse(last.NewSeason, "Entering month 2 stays in Waking, so this is not a season boundary.");
            Assert.IsFalse(last.NewYear);
        }

        [Test]
        public void Tick_ReportsSeasonBoundary_EnteringBloomAtMonthFour()
        {
            var clock = new SimClock();
            TickBoundaries last = default;
            for (long i = 0; i < CalendarConstants.TicksPerMonth * 3; i++)
            {
                last = clock.Tick();
            }

            Assert.IsTrue(last.NewDay);
            Assert.IsTrue(last.NewMonth);
            Assert.IsTrue(last.NewSeason);
            Assert.IsFalse(last.NewYear);
        }

        [Test]
        public void Tick_ReportsTheHollowAsBothANewMonthAndANewSeason()
        {
            var clock = new SimClock();
            TickBoundaries last = default;
            for (long i = 0; i < CalendarConstants.TicksPerMonth * 12; i++)
            {
                last = clock.Tick();
            }

            Assert.IsTrue(last.NewMonth);
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

            Assert.IsTrue(last.NewMonth);
            Assert.IsTrue(last.NewSeason);
            Assert.IsTrue(last.NewYear);
        }

        [Test]
        public void Tick_RollingThroughAFullYear_FiresEveryCalendarBoundaryExactlyOnceAtTheRightTick()
        {
            var clock = new SimClock();
            var dayTicks = new List<long>();
            var monthTicks = new List<long>();
            var seasonTicks = new List<long>();
            var yearTicks = new List<long>();

            for (long i = 0; i < CalendarConstants.TicksPerYear; i++)
            {
                TickBoundaries boundaries = clock.Tick();
                long tick = clock.CurrentTick;

                if (boundaries.NewDay) dayTicks.Add(tick);
                if (boundaries.NewMonth) monthTicks.Add(tick);
                if (boundaries.NewSeason) seasonTicks.Add(tick);
                if (boundaries.NewYear) yearTicks.Add(tick);
            }

            Assert.AreEqual(364, dayTicks.Count, "364 days in a year.");
            Assert.AreEqual(13, monthTicks.Count);
            Assert.AreEqual(5, seasonTicks.Count, "Entering Bloom, Reaping, Frost, the Hollow, and back to Waking at year's end.");
            Assert.AreEqual(1, yearTicks.Count);

            var expectedMonthTicks = new List<long>();
            for (int month = 1; month <= CalendarConstants.MonthsPerYear; month++)
            {
                expectedMonthTicks.Add(month * CalendarConstants.TicksPerMonth);
            }

            CollectionAssert.AreEqual(expectedMonthTicks, monthTicks);

            var expectedSeasonTicks = new List<long>
            {
                3 * CalendarConstants.TicksPerMonth,  // entering Bloom (month 4)
                6 * CalendarConstants.TicksPerMonth,  // entering Reaping (month 7)
                9 * CalendarConstants.TicksPerMonth,  // entering Frost (month 10)
                12 * CalendarConstants.TicksPerMonth, // entering the Hollow (month 13)
                13 * CalendarConstants.TicksPerMonth, // back to Waking, year 2
            };

            CollectionAssert.AreEqual(expectedSeasonTicks, seasonTicks);
            Assert.AreEqual(CalendarConstants.TicksPerYear, yearTicks[0]);
            Assert.Contains(12 * CalendarConstants.TicksPerMonth, monthTicks, "The Hollow begins at month 13, which is also a month boundary.");
        }
    }
}
