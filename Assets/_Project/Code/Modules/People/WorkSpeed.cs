namespace TheEmptying.Modules.People
{
    /// <summary>Converts a skill level into a work speed multiplier. Not yet exercised by any system; jobs arrive in 3.8.</summary>
    public static class WorkSpeed
    {
        public const int SkillCount = 17;
        public const int MaxSkillLevel = 20;

        /// <summary>50% at level 0, 100% at level 10, 150% at level 20.</summary>
        public static double ForSkillLevel(int level) => 0.5 + 0.05 * level;
    }
}
