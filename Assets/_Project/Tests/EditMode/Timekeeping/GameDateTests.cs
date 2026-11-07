using NUnit.Framework;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Timekeeping
{
    public class GameDateTests
    {
        [Test]
        public void TickZero_IsYear1Spring1Midnight()
        {
            GameDate date = GameDate.FromTick(0);

            Assert.AreEqual(1, date.Year);
            Assert.AreEqual(Season.Spring, date.Season);
            Assert.AreEqual(1, date.DayOfSeason);
            Assert.AreEqual(0, date.Hour);
            Assert.AreEqual(0, date.Minute);
        }

        [Test]
        public void HourBoundary_LastAndFirstTick()
        {
            GameDate last = GameDate.FromTick(CalendarConstants.TicksPerHour - 1);
            Assert.AreEqual(0, last.Hour);
            Assert.AreEqual(59, last.Minute);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerHour);
            Assert.AreEqual(1, first.Hour);
            Assert.AreEqual(0, first.Minute);
        }

        [Test]
        public void DayBoundary_LastAndFirstTick()
        {
            GameDate last = GameDate.FromTick(CalendarConstants.TicksPerDay - 1);
            Assert.AreEqual(1, last.DayOfSeason);
            Assert.AreEqual(23, last.Hour);
            Assert.AreEqual(59, last.Minute);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerDay);
            Assert.AreEqual(2, first.DayOfSeason);
            Assert.AreEqual(0, first.Hour);
            Assert.AreEqual(0, first.Minute);
        }

        [Test]
        public void SeasonBoundary_LastAndFirstTick()
        {
            GameDate last = GameDate.FromTick(CalendarConstants.TicksPerSeason - 1);
            Assert.AreEqual(Season.Spring, last.Season);
            Assert.AreEqual(CalendarConstants.DaysPerSeason, last.DayOfSeason);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerSeason);
            Assert.AreEqual(Season.Summer, first.Season);
            Assert.AreEqual(1, first.DayOfSeason);
        }

        [Test]
        public void YearBoundary_LastAndFirstTick()
        {
            GameDate last = GameDate.FromTick(CalendarConstants.TicksPerYear - 1);
            Assert.AreEqual(1, last.Year);
            Assert.AreEqual(Season.Winter, last.Season);
            Assert.AreEqual(CalendarConstants.DaysPerSeason, last.DayOfSeason);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerYear);
            Assert.AreEqual(2, first.Year);
            Assert.AreEqual(Season.Spring, first.Season);
            Assert.AreEqual(1, first.DayOfSeason);
        }
    }
}
