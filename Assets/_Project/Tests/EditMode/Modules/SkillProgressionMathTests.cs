using NUnit.Framework;
using TheEmptying.Modules.People;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>R13: the experience curve - the sum of every level's hours (1.25x longer each time) must match the "about N years of full-time work" target exactly, for any growth factor and level count.</summary>
    public class SkillProgressionMathTests
    {
        [Test]
        public void BaseHoursForFirstLevel_SummedAcrossEveryLevel_MatchesTheYearsToMasterTarget()
        {
            const double yearsToMaster = 10.0;
            const double workHoursPerDay = 8.0;
            const double daysPerYear = 364.0; // 28 days x 13 months, this project's calendar.
            const double growthFactor = 1.25;
            const int maxLevel = 20;

            double baseHours = SkillProgressionMath.BaseHoursForFirstLevel(yearsToMaster, workHoursPerDay, daysPerYear, growthFactor, maxLevel);

            double total = 0;
            for (int level = 1; level < maxLevel; level++)
            {
                total += SkillProgressionMath.HoursForLevel(level, baseHours, growthFactor);
            }

            double expectedTotal = SkillProgressionMath.TotalHoursToMaster(yearsToMaster, workHoursPerDay, daysPerYear);
            Assert.AreEqual(expectedTotal, total, 1e-6);
        }

        [Test]
        public void HoursForLevel_EachLevelIsGrowthFactorTimesTheLast()
        {
            const double baseHours = 100.0;
            const double growthFactor = 1.25;

            double level1 = SkillProgressionMath.HoursForLevel(1, baseHours, growthFactor);
            double level2 = SkillProgressionMath.HoursForLevel(2, baseHours, growthFactor);
            double level3 = SkillProgressionMath.HoursForLevel(3, baseHours, growthFactor);

            Assert.AreEqual(baseHours, level1, 1e-9);
            Assert.AreEqual(level1 * growthFactor, level2, 1e-9);
            Assert.AreEqual(level2 * growthFactor, level3, 1e-9);
        }

        [TestCase(1.0, 5)]
        [TestCase(1.5, 10)]
        [TestCase(2.0, 1)]
        public void SumOfGeometricSeries_MatchesTheNaiveSum(double growthFactor, int termCount)
        {
            double naive = 0;
            for (int i = 0; i < termCount; i++)
            {
                naive += System.Math.Pow(growthFactor, i);
            }

            Assert.AreEqual(naive, SkillProgressionMath.SumOfGeometricSeries(growthFactor, termCount), 1e-9);
        }
    }
}
