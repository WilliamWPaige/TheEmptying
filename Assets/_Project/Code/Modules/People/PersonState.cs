using System.Collections.Generic;
using TheEmptying.Core.Content;

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

        /// <summary>An unpaid crown servant, spawned at new-game start: lower class forever, owns nothing, maxed in a handful of labor skills.</summary>
        public bool IsFounder;

        /// <summary>The separate 9th starting person: upper class, no job, lives in the keep.</summary>
        public bool IsMonarch;

        /// <summary>Set for founders: never quits, never refuses an order, always passes a willpower check. Needs still drop and neglect can still kill them.</summary>
        public bool NeverRefusesOrFails;

        /// <summary>3.9j: the one core trade skill this founder was rolled extra-high in (19-20, instead of the usual 14-18); null for anyone who isn't a founder.</summary>
        public Skill? FounderSpecialty;

        /// <summary>Overrides which class's needs apply, independent of social class - the advisor is upper class in name but keeps lower-class needs. Null (the default) means "use the real social class".</summary>
        public SocialClass? NeedsClassOverride;

        /// <summary>One entry per need this person's class (and, for faith, believer status) makes applicable. Keyed by the need's content Id.</summary>
        public readonly Dictionary<string, PersonNeedState> Needs = new Dictionary<string, PersonNeedState>();

        public readonly int[] Skills = new int[WorkSpeed.SkillCount];

        /// <summary>Fractional progress (0..1) toward each skill's next level, accumulated from work and applied once it crosses 1.</summary>
        public readonly double[] SkillProgress = new double[WorkSpeed.SkillCount];

        /// <summary>Consecutive hours with hunger at zero; resets the moment hunger rises above zero. Drives starvation death.</summary>
        public double HoursHungerEmpty;

        /// <summary>Hours left of the mood penalty from eating a ground grub (people prefer bread); 0 means no penalty is active.</summary>
        public int GrubMoodPenaltyHoursRemaining;

        /// <summary>Hours of self-foraging already spent toward the next grub, while hungry with no bread; resets once a grub is dug up.</summary>
        public double GrubDiggingHoursAccumulated;

        // Monthly lifestyle-shift accumulators, reset after each shift is applied.
        public int HoursTrackedThisMonth;
        public int HoursHungerCriticalThisMonth;
        public int HomelessNightsThisMonth;
        public double HygieneSumThisMonth;
        public int HygieneSamplesThisMonth;
        public int HoursBadMoodThisMonth;
        public int HardLaborHoursThisMonth;
    }
}
