using System.Collections.Generic;
using TheEmptying.Modules.Map;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// A person (R7: type and world position; R8: name, sex, age, role,
    /// specialty and stats; R10: current action, movement and history;
    /// R13: hunger, carried goods, skill experience, and likes/dislikes;
    /// R13b: the home stash itself moved to PeopleModule, shared by
    /// everyone living at the same home building). Identity (name, sex,
    /// age, role, specialty) is set
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

        /// <summary>R13: items currently carried (foraged for the stockpile, extra grubs on the way home, or an incidental bonus pickup like flint while gathering rock), by item content Id; empty when carrying nothing. R15 generalized this from a single item+amount pair, since one gathering trip can yield a bonus item alongside the primary good.</summary>
        public Dictionary<string, int> CarriedItems { get; } = new Dictionary<string, int>();

        public int CarriedAmountOf(string itemContentId) => CarriedItems.TryGetValue(itemContentId, out int amount) ? amount : 0;

        public void AddCarried(string itemContentId, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemContentId))
            {
                return;
            }

            CarriedItems.TryGetValue(itemContentId, out int current);
            CarriedItems[itemContentId] = current + amount;
        }

        public void RemoveCarried(string itemContentId, int amount)
        {
            if (!CarriedItems.TryGetValue(itemContentId, out int current))
            {
                return;
            }

            int remaining = current - amount;
            if (remaining <= 0)
            {
                CarriedItems.Remove(itemContentId);
            }
            else
            {
                CarriedItems[itemContentId] = remaining;
            }
        }

        public bool IsCarryingAnything => CarriedItems.Count > 0;

        /// <summary>R15: which item (grub, stick or rock) the current ForagingForStockpile trip is gathering - chosen once when the goal starts, so an interrupted/resumed trip keeps gathering the same thing rather than re-rolling.</summary>
        public string ForagingItemId { get; set; } = string.Empty;

        /// <summary>R15: true while the current Eating action is eating directly from carried goods (a hungry person carrying grubs eats from them first, wherever they are), rather than from a stockpile or the home stash.</summary>
        public bool EatingFromCarried { get; set; }

        /// <summary>R15: this person's own coin, in tenths of a copper (so 5 sticks at 0.2 copper each pays out exactly 1 copper, with no fraction ever lost). Paid cash on delivery whenever they drop base goods in a crown stockpile.</summary>
        public int WalletTenthCopper { get; set; }

        /// <summary>R13: the building this person lives in - for now, every founder and the Monarch live in the keep. Null for a person with no home (e.g. the Spawn cheat).</summary>
        public EntityId? HomeBuildingId { get; set; }

        /// <summary>R13: what this person is currently trying to accomplish - see PersonGoal.</summary>
        public PersonGoal Goal { get; set; } = PersonGoal.None;

        /// <summary>R13: the stockpile currently being walked to/eaten from, while SeekingFood; null otherwise. A separate field from ForagingDeliveryZoneId so an eating trip and a delivery trip never overwrite each other's target.</summary>
        public EntityId? TargetZoneId { get; set; }

        /// <summary>R13: the stockpile currently being delivered to, while ForagingForStockpile; null otherwise.</summary>
        public EntityId? ForagingDeliveryZoneId { get; set; }

        /// <summary>R13: true while the current SeekingFood trip's destination is this person's own home stash.</summary>
        public bool TargetIsHome { get; set; }

        /// <summary>R13: true while the current SeekingFood trip's destination is a grub mound (SeekingFood only - ToppingUpHomeStash always digs at a mound first, decided by carried amount, not this flag; ForagingForStockpile decides by ForagingItemId instead, since R15 it may be gathering sticks or rocks instead of digging grubs).</summary>
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
