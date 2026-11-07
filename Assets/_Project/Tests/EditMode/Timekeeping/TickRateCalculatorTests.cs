using NUnit.Framework;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Timekeeping
{
    public class TickRateCalculatorTests
    {
        [Test]
        public void TicksPerRealSecond_MatchesDefaultDayLength()
        {
            Assert.AreEqual(20.0, TickRateCalculator.TicksPerRealSecond(720, SimSpeed.Normal), 0.0001);
            Assert.AreEqual(40.0, TickRateCalculator.TicksPerRealSecond(720, SimSpeed.Fast), 0.0001);
            Assert.AreEqual(60.0, TickRateCalculator.TicksPerRealSecond(720, SimSpeed.Fastest), 0.0001);
        }

        [Test]
        public void TicksPerRealSecond_Paused_IsZero()
        {
            Assert.AreEqual(0.0, TickRateCalculator.TicksPerRealSecond(720, SimSpeed.Paused), 0.0001);
        }

        [Test]
        public void TicksToRun_BelowCap_ConsumesExactTimeAndCarriesLeftover()
        {
            int ticks = TickRateCalculator.TicksToRun(0.12, 20.0, 1000, out double leftover);

            Assert.AreEqual(2, ticks);
            Assert.AreEqual(0.02, leftover, 0.0001);
        }

        [Test]
        public void TicksToRun_AboveCap_CapsAndCarriesRemainingTime()
        {
            int ticks = TickRateCalculator.TicksToRun(10.0, 20.0, 5, out double leftover);

            Assert.AreEqual(5, ticks);
            Assert.AreEqual(10.0 - 5.0 / 20.0, leftover, 0.0001);
        }

        [Test]
        public void TicksToRun_Paused_ReturnsZeroAndKeepsAccumulatedTime()
        {
            int ticks = TickRateCalculator.TicksToRun(5.0, 0.0, 10, out double leftover);

            Assert.AreEqual(0, ticks);
            Assert.AreEqual(5.0, leftover, 0.0001);
        }
    }
}
