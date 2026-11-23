using System;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// R13: the experience curve - going from skill 1 to the cap takes
    /// about a target number of game years of full-time work, each level
    /// taking a fixed factor longer than the one before. Pure math so the
    /// curve (and the "about N years" target) is directly testable.
    /// </summary>
    public static class SkillProgressionMath
    {
        public static double TotalHoursToMaster(double yearsToMaster, double workHoursPerDay, double daysPerYear) =>
            yearsToMaster * daysPerYear * workHoursPerDay;

        /// <summary>1 + growthFactor + growthFactor^2 + ... + growthFactor^(termCount-1).</summary>
        public static double SumOfGeometricSeries(double growthFactor, int termCount)
        {
            if (termCount <= 0)
            {
                return 0;
            }

            if (Math.Abs(growthFactor - 1.0) < 1e-9)
            {
                return termCount;
            }

            return (Math.Pow(growthFactor, termCount) - 1.0) / (growthFactor - 1.0);
        }

        /// <summary>
        /// Hours needed for the very first level-up (level 1 to 2), worked
        /// backwards from the total-hours target so that every level from 1
        /// to maxLevel, each growthFactor times slower than the last, sums
        /// to exactly that target.
        /// </summary>
        public static double BaseHoursForFirstLevel(double yearsToMaster, double workHoursPerDay, double daysPerYear, double growthFactor, int maxLevel)
        {
            int transitions = Math.Max(0, maxLevel - 1);
            double totalHours = TotalHoursToMaster(yearsToMaster, workHoursPerDay, daysPerYear);
            double sum = SumOfGeometricSeries(growthFactor, transitions);
            return sum <= 0 ? 0 : totalHours / sum;
        }

        /// <summary>Hours needed to go from the given level to the next one (level 1-based; the hours for 1-&gt;2 is baseHoursForFirstLevel itself).</summary>
        public static double HoursForLevel(int level, double baseHoursForFirstLevel, double growthFactor)
        {
            if (level < 1)
            {
                return baseHoursForFirstLevel;
            }

            return baseHoursForFirstLevel * Math.Pow(growthFactor, level - 1);
        }
    }
}
