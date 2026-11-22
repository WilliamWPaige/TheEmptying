using System.Collections.Generic;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// A person (R7: type and world position; R8: name, sex, age, role,
    /// specialty and stats). Immutable once rolled - nothing here changes
    /// over time yet.
    /// </summary>
    public sealed class PersonState
    {
        public string UnitContentId { get; }
        public double WorldX { get; }
        public double WorldZ { get; }
        public string Name { get; }
        public PersonSex Sex { get; }
        public int AgeYears { get; }
        public PersonRole Role { get; }

        /// <summary>A founder's (or the Advisor's) one random specialty skill Id; empty for the Monarch.</summary>
        public string SpecialtySkillId { get; }

        public IReadOnlyDictionary<string, int> Skills { get; }
        public IReadOnlyDictionary<string, int> Attributes { get; }

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
