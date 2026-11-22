using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// People (R7: entity and world position; R8: identity and stats).
    /// Nothing else yet - no needs, jobs or movement. Depends on Map, to
    /// find a free cell (not on a tree, stone or mound) to stand a person
    /// on.
    /// </summary>
    public sealed class PeopleModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.people";
        public const string PersonUnitContentId = "unit.person";

        public const string FounderCountSettingId = "setting.people.founder_count";
        public const string FounderAgeMinSettingId = "setting.people.founder_age_min";
        public const string FounderAgeMaxSettingId = "setting.people.founder_age_max";
        public const string FounderCoreTradeMinSettingId = "setting.people.founder_core_trade_min";
        public const string FounderCoreTradeMaxSettingId = "setting.people.founder_core_trade_max";
        public const string FounderSpecialtyMinSettingId = "setting.people.founder_specialty_min";
        public const string FounderSpecialtyMaxSettingId = "setting.people.founder_specialty_max";
        public const string FounderOtherSkillMinSettingId = "setting.people.founder_other_skill_min";
        public const string FounderOtherSkillMaxSettingId = "setting.people.founder_other_skill_max";
        public const string FounderAttributeMinSettingId = "setting.people.founder_attribute_min";
        public const string FounderAttributeMaxSettingId = "setting.people.founder_attribute_max";
        public const string MonarchAgeMinSettingId = "setting.people.monarch_age_min";
        public const string MonarchAgeMaxSettingId = "setting.people.monarch_age_max";
        public const string MonarchLeadershipSpeechMinSettingId = "setting.people.monarch_leadership_speech_min";
        public const string MonarchLeadershipSpeechMaxSettingId = "setting.people.monarch_leadership_speech_max";
        public const string MonarchOtherSkillMinSettingId = "setting.people.monarch_other_skill_min";
        public const string MonarchOtherSkillMaxSettingId = "setting.people.monarch_other_skill_max";
        public const string MonarchAttributeMinSettingId = "setting.people.monarch_attribute_min";
        public const string MonarchAttributeMaxSettingId = "setting.people.monarch_attribute_max";

        /// <summary>The six skills every founder (and the Advisor) is good at by design - not a setting, the design names these specific skills.</summary>
        private static readonly HashSet<string> CoreTradeSkillIds = new HashSet<string>
        {
            "skill.logging", "skill.quarrying", "skill.farming", "skill.foraging", "skill.hauling", "skill.construction"
        };

        private const string LeadershipSkillId = "skill.leadership";
        private const string SpeechSkillId = "skill.speech";

        /// <summary>Placeholder for "a few meters apart" - cells and meters are the same at the default 1 m cell size.</summary>
        private const int FounderSpacingCells = 3;

        private const int MaxSearchRadiusCells = 256;

        private readonly MapModule _mapModule;
        private EntityRegistry _entities;
        private SettingsRegistry _settings;
        private RandomService _randomService;
        private ContentDatabase _content;
        private EntityTable<PersonState> _people;

        public PeopleModule(MapModule mapModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId };
        public int SectionVersion => 2;

        /// <summary>Every current person's Id and state, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, PersonState>> People => _people.Rows();

        public bool TryGetPerson(EntityId id, out PersonState person) => _people.TryGet(id, out person);

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _settings = context.Settings;
            _randomService = context.RandomService;
            _content = context.Content;
            _people = new EntityTable<PersonState>(_entities);

            if (!context.IsLoading)
            {
                SpawnFounders();
            }
        }

        public void Shutdown()
        {
        }

        /// <summary>Spawns a Founder on the nearest free cell to a world position (the Spawn cheat's "at the camera center"); null if none could be found.</summary>
        public EntityId? TrySpawnPersonNear(double worldX, double worldZ)
        {
            CellCoord center = _mapModule.WorldToCell(worldX, worldZ);
            List<CellCoord> cells = FindFreeCells(center, 1, minSpacingCells: 0);
            if (cells.Count == 0)
            {
                return null;
            }

            SimRandom random = _randomService.GetStream("people.rolls");
            PersonState state = RollPerson(random, PersonRole.Founder, cells[0]);
            return SpawnPersonWithState(state);
        }

        public void RemovePerson(EntityId id)
        {
            if (_people.Has(id))
            {
                _entities.Destroy(id);
            }
        }

        private void SpawnFounders()
        {
            int count = (int)_settings.Get(FounderCountSettingId);
            CellCoord center = _mapModule.WorldToCell(_mapModule.WorldWidthMeters * 0.5, _mapModule.WorldDepthMeters * 0.5);
            List<CellCoord> cells = FindFreeCells(center, count, FounderSpacingCells);

            SimRandom random = _randomService.GetStream("people.rolls");

            for (int i = 0; i < cells.Count; i++)
            {
                PersonRole role = i == 0 ? PersonRole.Monarch : i == 1 ? PersonRole.Advisor : PersonRole.Founder;
                PersonState state = RollPerson(random, role, cells[i]);
                SpawnPersonWithState(state);
            }
        }

        private EntityId SpawnPersonWithState(PersonState state)
        {
            EntityId id = _entities.Create(EntityKind.Person);
            _people.Add(id, state);
            return id;
        }

        /// <summary>
        /// Rolls a new person's identity and stats. The Advisor is a
        /// founder and rolls like one (R8) - only the Monarch uses the
        /// different range. Every roll comes from the given stream, in a
        /// fixed order, so the same world seed always gives the same
        /// people.
        /// </summary>
        private PersonState RollPerson(SimRandom random, PersonRole role, CellCoord cell)
        {
            List<SkillDefinition> skills = BuiltInContentTypes.OrderedSkills(_content);
            List<AttributeDefinition> attributes = BuiltInContentTypes.OrderedAttributes(_content);

            PersonSex sex = random.Chance(0.5f) ? PersonSex.Male : PersonSex.Female;
            string name = PickName(random, sex);

            bool isMonarch = role == PersonRole.Monarch;

            int ageMin = (int)_settings.Get(isMonarch ? MonarchAgeMinSettingId : FounderAgeMinSettingId);
            int ageMax = (int)_settings.Get(isMonarch ? MonarchAgeMaxSettingId : FounderAgeMaxSettingId);
            int age = random.NextInt(ageMin, ageMax + 1);

            var skillValues = new Dictionary<string, int>();
            var attributeValues = new Dictionary<string, int>();
            string specialtyId = string.Empty;

            if (isMonarch)
            {
                int leadershipSpeechMin = (int)_settings.Get(MonarchLeadershipSpeechMinSettingId);
                int leadershipSpeechMax = (int)_settings.Get(MonarchLeadershipSpeechMaxSettingId);
                int otherMin = (int)_settings.Get(MonarchOtherSkillMinSettingId);
                int otherMax = (int)_settings.Get(MonarchOtherSkillMaxSettingId);

                foreach (SkillDefinition skill in skills)
                {
                    bool isLeadershipOrSpeech = skill.Id == LeadershipSkillId || skill.Id == SpeechSkillId;
                    skillValues[skill.Id] = isLeadershipOrSpeech
                        ? random.NextInt(leadershipSpeechMin, leadershipSpeechMax + 1)
                        : random.NextInt(otherMin, otherMax + 1);
                }

                int attrMin = (int)_settings.Get(MonarchAttributeMinSettingId);
                int attrMax = (int)_settings.Get(MonarchAttributeMaxSettingId);
                foreach (AttributeDefinition attribute in attributes)
                {
                    attributeValues[attribute.Id] = random.NextInt(attrMin, attrMax + 1);
                }
            }
            else
            {
                int coreMin = (int)_settings.Get(FounderCoreTradeMinSettingId);
                int coreMax = (int)_settings.Get(FounderCoreTradeMaxSettingId);
                int specialtyMin = (int)_settings.Get(FounderSpecialtyMinSettingId);
                int specialtyMax = (int)_settings.Get(FounderSpecialtyMaxSettingId);
                int otherMin = (int)_settings.Get(FounderOtherSkillMinSettingId);
                int otherMax = (int)_settings.Get(FounderOtherSkillMaxSettingId);

                List<SkillDefinition> nonCoreSkills = skills.Where(s => !CoreTradeSkillIds.Contains(s.Id)).ToList();
                SkillDefinition specialty = nonCoreSkills[random.NextInt(0, nonCoreSkills.Count)];
                specialtyId = specialty.Id;

                foreach (SkillDefinition skill in skills)
                {
                    int value;
                    if (CoreTradeSkillIds.Contains(skill.Id))
                    {
                        value = random.NextInt(coreMin, coreMax + 1);
                    }
                    else if (skill.Id == specialtyId)
                    {
                        value = random.NextInt(specialtyMin, specialtyMax + 1);
                    }
                    else
                    {
                        value = random.NextInt(otherMin, otherMax + 1);
                    }

                    skillValues[skill.Id] = value;
                }

                int attrMin = (int)_settings.Get(FounderAttributeMinSettingId);
                int attrMax = (int)_settings.Get(FounderAttributeMaxSettingId);
                foreach (AttributeDefinition attribute in attributes)
                {
                    attributeValues[attribute.Id] = random.NextInt(attrMin, attrMax + 1);
                }
            }

            double cellSize = _mapModule.CellSizeMeters;
            double worldX = (cell.X + 0.5) * cellSize;
            double worldZ = (cell.Z + 0.5) * cellSize;

            return new PersonState(PersonUnitContentId, worldX, worldZ, name, sex, age, role, specialtyId, skillValues, attributeValues);
        }

        private string PickName(SimRandom random, PersonSex sex)
        {
            NameListKind firstNameKind = sex == PersonSex.Male ? NameListKind.MaleFirstName : NameListKind.FemaleFirstName;
            List<string> firstNames = BuiltInContentTypes.NamesOfKind(_content, firstNameKind);
            List<string> familyNames = BuiltInContentTypes.NamesOfKind(_content, NameListKind.FamilyName);

            string first = firstNames.Count > 0 ? firstNames[random.NextInt(0, firstNames.Count)] : "Unnamed";
            string last = familyNames.Count > 0 ? familyNames[random.NextInt(0, familyNames.Count)] : string.Empty;

            return string.IsNullOrEmpty(last) ? first : $"{first} {last}";
        }

        /// <summary>
        /// Free cells (not on a tree, stone or mound), nearest to center
        /// first, at least minSpacingCells apart from each other - an
        /// expanding ring search, so the result is fully deterministic
        /// (no random rolls needed to place founders a few meters apart).
        /// </summary>
        private List<CellCoord> FindFreeCells(CellCoord center, int count, int minSpacingCells)
        {
            var accepted = new List<CellCoord>();

            for (int radius = 0; radius <= MaxSearchRadiusCells && accepted.Count < count; radius++)
            {
                foreach (CellCoord candidate in RingCells(center, radius))
                {
                    if (accepted.Count >= count)
                    {
                        break;
                    }

                    if (!_mapModule.IsCellFree(candidate))
                    {
                        continue;
                    }

                    if (IsTooCloseToAny(candidate, accepted, minSpacingCells))
                    {
                        continue;
                    }

                    accepted.Add(candidate);
                }
            }

            return accepted;
        }

        private static bool IsTooCloseToAny(CellCoord candidate, List<CellCoord> accepted, int minSpacingCells)
        {
            foreach (CellCoord other in accepted)
            {
                if (Math.Max(Math.Abs(other.X - candidate.X), Math.Abs(other.Z - candidate.Z)) < minSpacingCells)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The square ring of cells exactly radius cells from center (radius 0 is just center itself).</summary>
        private static IEnumerable<CellCoord> RingCells(CellCoord center, int radius)
        {
            if (radius == 0)
            {
                yield return center;
                yield break;
            }

            for (int dx = -radius; dx <= radius; dx++)
            {
                yield return new CellCoord(center.X + dx, center.Z - radius);
                yield return new CellCoord(center.X + dx, center.Z + radius);
            }

            for (int dz = -radius + 1; dz <= radius - 1; dz++)
            {
                yield return new CellCoord(center.X - radius, center.Z + dz);
                yield return new CellCoord(center.X + radius, center.Z + dz);
            }
        }

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_people.Count);
            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows())
            {
                writer.WriteEntityId(row.Key);
                PersonState state = row.Value;

                writer.WriteString(state.UnitContentId);
                writer.WriteDouble(state.WorldX);
                writer.WriteDouble(state.WorldZ);
                writer.WriteString(state.Name);
                writer.WriteInt((int)state.Sex);
                writer.WriteInt(state.AgeYears);
                writer.WriteInt((int)state.Role);
                writer.WriteString(state.SpecialtySkillId);
                WriteStringIntMap(writer, state.Skills);
                WriteStringIntMap(writer, state.Attributes);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                string unitContentId = reader.ReadString();
                double worldX = reader.ReadDouble();
                double worldZ = reader.ReadDouble();
                string name = reader.ReadString();
                var sex = (PersonSex)reader.ReadInt();
                int age = reader.ReadInt();
                var role = (PersonRole)reader.ReadInt();
                string specialtyId = reader.ReadString();
                Dictionary<string, int> skills = ReadStringIntMap(reader);
                Dictionary<string, int> attributes = ReadStringIntMap(reader);

                _people.Add(id, new PersonState(unitContentId, worldX, worldZ, name, sex, age, role, specialtyId, skills, attributes));
            }
        }

        private static void WriteStringIntMap(SaveWriter writer, IReadOnlyDictionary<string, int> map)
        {
            writer.WriteInt(map.Count);
            foreach (KeyValuePair<string, int> kvp in map)
            {
                writer.WriteString(kvp.Key);
                writer.WriteInt(kvp.Value);
            }
        }

        private static Dictionary<string, int> ReadStringIntMap(SaveReader reader)
        {
            int count = reader.ReadInt();
            var map = new Dictionary<string, int>(count);
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                int value = reader.ReadInt();
                map[key] = value;
            }

            return map;
        }
    }
}
