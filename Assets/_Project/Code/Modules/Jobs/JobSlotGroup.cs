using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Modules.People;

namespace TheEmptying.Modules.Jobs
{
    /// <summary>
    /// Every slot of one type (worker, manager or trainer) at one building:
    /// the deal offered (tier, skill, pay, coverage, minimum on duty) and who
    /// currently fills each of the building type's slots of this kind.
    /// </summary>
    public sealed class JobSlotGroup
    {
        public WorkerTier Tier;
        public Skill Skill;
        public Money PayPerDay;
        public JobCoverage Coverage;
        public int MinimumOnDuty;
        public int MinimumSkill;
        public int AllowedClassesMask = 7; // bitmask: 1=lower, 2=middle, 4=upper

        public List<EntityId?> Occupants = new List<EntityId?>();
        public List<int> UnpaidDaysInARow = new List<int>();
        public List<int> BadMoodDaysInARow = new List<int>();

        /// <summary>Cumulative skill-adjusted hours worked by this group, for 3.10's production to read; construction uses BuildingsModule's own labor hours instead.</summary>
        public double WorkHoursAccumulated;
    }
}
