using NUnit.Framework;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Timekeeping
{
    public class GameDateTests
    {
        [Test]
        public void TickZero_IsYear1Month1Waking_Day1Midnight()
        {
            GameDate date = GameDate.FromTick(0);

            Assert.AreEqual(1, date.Year);
            Assert.AreEqual(1, date.Month);
            Assert.AreEqual(Season.Waking, date.Season);
            Assert.AreEqual(1, date.DayOfMonth);
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
            Assert.AreEqual(1, last.DayOfMonth);
            Assert.AreEqual(23, last.Hour);
            Assert.AreEqual(59, last.Minute);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerDay);
            Assert.AreEqual(2, first.DayOfMonth);
            Assert.AreEqual(0, first.Hour);
            Assert.AreEqual(0, first.Minute);
        }

        [Test]
        public void MonthBoundary_LastAndFirstTick()
        {
            GameDate last = GameDate.FromTick(CalendarConstants.TicksPerMonth - 1);
            Assert.AreEqual(1, last.Month);
            Assert.AreEqual(Season.Waking, last.Season);
            Assert.AreEqual(CalendarConstants.DaysPerMonth, last.DayOfMonth);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerMonth);
            Assert.AreEqual(2, first.Month);
            Assert.AreEqual(Season.Waking, first.Season);
            Assert.AreEqual(1, first.DayOfMonth);
        }

        [Test]
        public void SeasonBoundary_WakingIntoBloom_LastAndFirstTick()
        {
            long lastTickOfMonth3 = CalendarConstants.TicksPerMonth * 3 - 1;
            GameDate last = GameDate.FromTick(lastTickOfMonth3);
            Assert.AreEqual(3, last.Month);
            Assert.AreEqual(Season.Waking, last.Season);
            Assert.AreEqual(CalendarConstants.DaysPerMonth, last.DayOfMonth);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerMonth * 3);
            Assert.AreEqual(4, first.Month);
            Assert.AreEqual(Season.Bloom, first.Season);
            Assert.AreEqual(1, first.DayOfMonth);
        }

        [Test]
        public void TheHollow_IsMonthThirteen_AndItsOwnSeason()
        {
            long lastTickOfMonth12 = CalendarConstants.TicksPerMonth * 12 - 1;
            GameDate last = GameDate.FromTick(lastTickOfMonth12);
            Assert.AreEqual(12, last.Month);
            Assert.AreEqual(Season.Frost, last.Season);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerMonth * 12);
            Assert.AreEqual(13, first.Month);
            Assert.AreEqual(Season.Hollow, first.Season);
            Assert.AreEqual(1, first.DayOfMonth);
        }

        [Test]
        public void YearBoundary_LastAndFirstTick()
        {
            GameDate last = GameDate.FromTick(CalendarConstants.TicksPerYear - 1);
            Assert.AreEqual(1, last.Year);
            Assert.AreEqual(13, last.Month);
            Assert.AreEqual(Season.Hollow, last.Season);
            Assert.AreEqual(CalendarConstants.DaysPerMonth, last.DayOfMonth);

            GameDate first = GameDate.FromTick(CalendarConstants.TicksPerYear);
            Assert.AreEqual(2, first.Year);
            Assert.AreEqual(1, first.Month);
            Assert.AreEqual(Season.Waking, first.Season);
            Assert.AreEqual(1, first.DayOfMonth);
        }

        [Test]
        public void ToString_NormalMonth_ShowsDaySeasonMonthAndYear()
        {
            var date = new GameDate(1, 4, Season.Bloom, 5, 0, 0);
            Assert.AreEqual("Day 5, Bloom (month 4), Year 1", date.ToString());
        }

        [Test]
        public void ToString_TheHollow_OmitsTheMonthNumber()
        {
            var date = new GameDate(1, 13, Season.Hollow, 12, 0, 0);
            Assert.AreEqual("Day 12, the Hollow, Year 1", date.ToString());
        }

        /// <summary>3.9i: the clock defaults to 12-hour time with AM/PM.</summary>
        [Test]
        public void ToClockString_TwelveHour_FormatsNoonAndMidnightCorrectly()
        {
            var noon = new GameDate(1, 1, Season.Waking, 1, 12, 0);
            var midnight = new GameDate(1, 1, Season.Waking, 1, 0, 0);
            var morning = new GameDate(1, 1, Season.Waking, 1, 7, 30);
            var evening = new GameDate(1, 1, Season.Waking, 1, 23, 5);

            Assert.AreEqual("12:00 PM", noon.ToClockString(use24Hour: false));
            Assert.AreEqual("12:00 AM", midnight.ToClockString(use24Hour: false));
            Assert.AreEqual("7:30 AM", morning.ToClockString(use24Hour: false));
            Assert.AreEqual("11:05 PM", evening.ToClockString(use24Hour: false));
        }

        [Test]
        public void ToClockString_TwentyFourHour_FormatsWithLeadingZeros()
        {
            var noon = new GameDate(1, 1, Season.Waking, 1, 12, 0);
            var midnight = new GameDate(1, 1, Season.Waking, 1, 0, 0);
            var evening = new GameDate(1, 1, Season.Waking, 1, 23, 5);

            Assert.AreEqual("12:00", noon.ToClockString(use24Hour: true));
            Assert.AreEqual("00:00", midnight.ToClockString(use24Hour: true));
            Assert.AreEqual("23:05", evening.ToClockString(use24Hour: true));
        }
    }
}
