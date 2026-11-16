using System.Collections.Generic;

namespace TheEmptying.Modules.People
{
    /// <summary>Everything about one person.</summary>
    public sealed class PersonState
    {
        public string FirstName;
        public string FamilyName;
        public string FullName => $"{FirstName} {FamilyName}";

        public double AgeLifeYears;
        public double LifespanYears;
        public double Health = 100.0;
        public bool IsBeliever;
        public double Mood = 100.0;
        public PersonActivity Activity = PersonActivity.Idle;

        /// <summary>One entry per need this person's class (and, for faith, believer status) makes applicable. Keyed by the need's content Id.</summary>
        public readonly Dictionary<string, PersonNeedState> Needs = new Dictionary<string, PersonNeedState>();

        public readonly int[] Skills = new int[WorkSpeed.SkillCount];

        /// <summary>Consecutive hours with hunger at zero; resets the moment hunger rises above zero. Drives starvation death.</summary>
        public double HoursHungerEmpty;

        // Monthly lifestyle-shift accumulators, reset after each shift is applied.
        public int HoursTrackedThisMonth;
        public int HoursHungerCriticalThisMonth;
        public int HomelessNightsThisMonth;
        public double HygieneSumThisMonth;
        public int HygieneSamplesThisMonth;
        public int HoursBadMoodThisMonth;
    }
}
