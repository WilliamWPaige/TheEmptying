using System.Collections.Generic;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// A person (R7: type and world position; R8: name, sex, age, role,
    /// specialty and stats; R10: current action, movement and history).
    /// Identity and stats are set once at creation and never change; world
    /// position and the action fields change every tick while walking.
    /// </summary>
    public sealed class PersonState
    {
        public string UnitContentId { get; }
        public double WorldX { get; set; }
        public double WorldZ { get; set; }
        public string Name { get; }
        public PersonSex Sex { get; }
        public int AgeYears { get; }
        public PersonRole Role { get; }

        /// <summary>A founder's (or the Advisor's) one random specialty skill Id; empty for the Monarch.</summary>
        public string SpecialtySkillId { get; }

        public IReadOnlyDictionary<string, int> Skills { get; }
        public IReadOnlyDictionary<string, int> Attributes { get; }

        /// <summary>R10: what this person is doing right now - never null once spawned.</summary>
        public PersonAction CurrentAction { get; set; }

        /// <summary>
        /// R10: the one cell nobody else may stand on or walk to while this
        /// person holds it - their current cell while Idle, or the cell
        /// they're walking to while Walk.
        /// </summary>
        public CellCoord ReservedCell { get; set; }

        /// <summary>R10: the last few finished actions, most recent first, capped by setting.people.action_history_count.</summary>
        public List<PersonActionHistoryEntry> History { get; } = new List<PersonActionHistoryEntry>();

        public PersonState(
            string unitContentId,
            double worldX,
            double worldZ,
            string name,
            PersonSex sex,
            int ageYears,
            PersonRole role,
            string specialtySkillId,
            IReadOnlyDictionary<string, int> skills,
            IReadOnlyDictionary<string, int> attributes)
        {
            UnitContentId = unitContentId;
            WorldX = worldX;
            WorldZ = worldZ;
            Name = name;
            Sex = sex;
            AgeYears = ageYears;
            Role = role;
            SpecialtySkillId = specialtySkillId ?? string.Empty;
            Skills = skills;
            Attributes = attributes;
        }
    }
}
