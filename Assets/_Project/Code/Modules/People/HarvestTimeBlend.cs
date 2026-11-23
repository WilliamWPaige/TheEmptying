using System;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// R13: blends a resource's Unskilled/Skilled/Master harvest times by
    /// a skill level (1-20) - piecewise linear between skill 1 (fully
    /// Unskilled), the midpoint of the 1-20 range (fully Skilled) and
    /// skill 20 (fully Master). Placeholder blend curve - no anchor
    /// points or shape were specified beyond the three named tiers.
    /// </summary>
    public static class HarvestTimeBlend
    {
        private const double UnskilledAnchor = 1.0;
        private const double SkilledAnchor = 10.5;
        private const double MasterAnchor = 20.0;

        public static double BlendMinutes(double skillLevel, double unskilledMinutes, double skilledMinutes, double masterMinutes)
        {
            if (skillLevel <= UnskilledAnchor)
            {
                return unskilledMinutes;
            }

            if (skillLevel >= MasterAnchor)
            {
                return masterMinutes;
            }

            if (skillLevel <= SkilledAnchor)
            {
                double t = (skillLevel - UnskilledAnchor) / (SkilledAnchor - UnskilledAnchor);
                return Lerp(unskilledMinutes, skilledMinutes, t);
            }

            double t2 = (skillLevel - SkilledAnchor) / (MasterAnchor - SkilledAnchor);
            return Lerp(skilledMinutes, masterMinutes, t2);
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
