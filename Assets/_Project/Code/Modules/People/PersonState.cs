using System.Collections.Generic;
using TheEmptying.Modules.Map;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// A person (R7: type and world position; R8: name, sex, age, role,
    /// specialty and stats; R10: current action, movement and history;
    /// R13: hunger, carried goods, a home stash, skill experience, and
    /// likes/dislikes). Identity (name, sex, age, role, specialty) is set
    /// once at creation and never changes; everything else changes over
    /// play - skill levels included, via IncreaseSkill.
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

        private readonly Dictionary<string, int> _skills;
        public IReadOnlyDictionary<string, int> Skills => _skills;
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

        /// <summary>R13: a short, non-action note (for now, only skill level-ups), most recent first, capped the same as History.</summary>
        public List<PersonNote> Notes { get; } = new List<PersonNote>();

        /// <summary>R13: 100 = full, 0 = starving. Drains every tick; restored by eating.</summary>
        public double Hunger { get; set; } = 100.0;

        /// <summary>R13: the item currently being carried (foraged for the stockpile, or extra grubs on the way home); empty when not carrying anything.</summary>
        public string CarriedItemId { get; set; } = string.Empty;

        public int CarriedAmount { get; set; }

        /// <summary>R13: the building this person lives in - for now, every founder and the Monarch live in the keep. Null for a person with no home (e.g. the Spawn cheat).</summary>
        public EntityId? HomeBuildingId { get; set; }

        /// <summary>R13: this person's own food stash, kept at their home, by item content Id.</summary>
        public Dictionary<string, int> HomeStash { get; } = new Dictionary<string, int>();

        /// <summary>R13: what this person is currently trying to accomplish - see PersonGoal.</summary>
        public PersonGoal Goal { get; set; } = PersonGoal.None;

        /// <summary>R13: the stockpile currently being walked to/eaten from, while SeekingFood; null otherwise. A separate field from ForagingDeliveryZoneId so an eating trip and a delivery trip never overwrite each other's target.</summary>
        public EntityId? TargetZoneId { get; set; }

        /// <summary>R13: the stockpile currently being delivered to, while ForagingForStockpile; null otherwise.</summary>
        public EntityId? ForagingDeliveryZoneId { get; set; }

        /// <summary>R13: true while the current SeekingFood trip's destination is this person's own home stash.</summary>
        public bool TargetIsHome { get; set; }

        /// <summary>R13: true while the current SeekingFood trip's destination is a grub mound (SeekingFood only - ToppingUpHomeStash and ForagingForStockpile always dig at a mound first, decided by carried amount, not this flag).</summary>
        public bool TargetIsMound { get; set; }

        /// <summary>R13: true if the most recent meal (while SeekingFood) was dug at a mound - decides whether becoming full leads into ToppingUpHomeStash.</summary>
        public bool LastAteFromMound { get; set; }

        /// <summary>R13: skills this person likes doing - rolled once at creation from the world seed. Never overlaps DislikedSkillIds.</summary>
        public List<string> LikedSkillIds { get; } = new List<string>();

        /// <summary>R13: skills this person dislikes doing - rolled once at creation from the world seed. Never overlaps LikedSkillIds.</summary>
        public List<string> DislikedSkillIds { get; } = new List<string>();

        /// <summary>R13: hours of work accumulated toward each skill's next level; reset to 0 (carrying over the remainder) whenever that skill levels up.</summary>
        public Dictionary<string, double> SkillExperienceHours { get; } = new Dictionary<string, double>();

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
            _skills = new Dictionary<string, int>(skills);
            Attributes = attributes;
        }

        /// <summary>R13: sets a skill to a new (higher) level - the only way Skills ever changes after creation.</summary>
        public void SetSkillLevel(string skillId, int level)
        {
            _skills[skillId] = level;
        }
    }
}
