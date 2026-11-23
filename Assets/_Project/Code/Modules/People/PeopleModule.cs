using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// People (R7: entity and world position; R8: identity and stats; R10:
    /// actions, real-time walking, idle milling, thoughts and history).
    /// Depends on Map, to find a free cell (not on a tree, stone or mound,
    /// and not already held by another person) to stand or walk a person
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

        /// <summary>R10: how fast a person walks, in meters per real second at 1x game speed (TEMPORARY placeholder).</summary>
        public const string WalkSpeedMpsSettingId = "setting.people.walk_speed_mps";

        /// <summary>R10: the most a single path search will cover, in meters (TEMPORARY placeholder).</summary>
        public const string PathfindingMaxDistanceMetersSettingId = "setting.people.pathfinding_max_distance_meters";

        /// <summary>R10: the shortest an idle person's wait can roll, in real seconds at 1x (TEMPORARY placeholder).</summary>
        public const string IdleWaitMinSecondsSettingId = "setting.people.idle_wait_min_seconds";

        /// <summary>R10: the longest an idle person's wait can roll, in real seconds at 1x (TEMPORARY placeholder).</summary>
        public const string IdleWaitMaxSecondsSettingId = "setting.people.idle_wait_max_seconds";

        /// <summary>R10: how far an idle person may wander on their own, in meters (TEMPORARY placeholder).</summary>
        public const string IdleWanderRadiusMetersSettingId = "setting.people.idle_wander_radius_meters";

        /// <summary>R10: how many of a person's most recent finished actions are kept for the info window (TEMPORARY placeholder).</summary>
        public const string ActionHistoryCountSettingId = "setting.people.action_history_count";

        /// <summary>Owned by time_settings.json, not by any module - the same real-time-to-tick conversion the presentation layer's tick rate uses.</summary>
        private const string RealSecondsPerDaySettingId = "setting.time.real_seconds_per_day";

        private const string RollsStreamName = "people.rolls";
        private const string ActionsStreamName = "people.actions";
        private const string ActionSystemId = "people.actions";

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
        private const int WanderTargetAttempts = 20;

        private readonly MapModule _mapModule;
        private EntityRegistry _entities;
        private SettingsRegistry _settings;
        private RandomService _randomService;
        private ContentDatabase _content;
        private ModuleContext _context;
        private EntityTable<PersonState> _people;

        /// <summary>The one cell each reserving person currently holds - their standing cell while Idle, or the cell they're walking to while Walk. Rebuilt from each person's own ReservedCell on load.</summary>
        private readonly Dictionary<CellCoord, EntityId> _reservedCells = new Dictionary<CellCoord, EntityId>();

        public PeopleModule(MapModule mapModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId };
        public int SectionVersion => 3;

        /// <summary>Every current person's Id and state, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, PersonState>> People => _people.Rows();

        public bool TryGetPerson(EntityId id, out PersonState person) => _people.TryGet(id, out person);

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _settings = context.Settings;
            _randomService = context.RandomService;
            _content = context.Content;
            _context = context;
            _people = new EntityTable<PersonState>(_entities);

            context.RegisterSystem(new PersonActionSystem(this), interval: 1, offset: 0, order: 0);

            if (!context.IsLoading)
            {
                SpawnFounders(context.CurrentTick);
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

            SimRandom random = _randomService.GetStream(RollsStreamName);
            PersonState state = RollPerson(random, PersonRole.Founder, cells[0]);
            return SpawnPersonWithState(state, _context.CurrentTick);
        }

        /// <summary>
        /// TEST TOOL (R10, never gameplay): starts the given person walking
        /// to the cell containing this world position, with the thought
        /// "Sent by test tool." Used by the People cheat's "Click to send"
        /// toggle.
        /// </summary>
        public void TrySendPersonTo(EntityId id, double worldX, double worldZ)
        {
            if (!_people.TryGet(id, out PersonState person))
            {
                return;
            }

            CellCoord target = _mapModule.WorldToCell(worldX, worldZ);
            StartWalk(id, person, target, "Sent by test tool.", _context.CurrentTick);
        }

        public void RemovePerson(EntityId id)
        {
            if (_people.TryGet(id, out PersonState state))
            {
                _reservedCells.Remove(state.ReservedCell);
                _entities.Destroy(id);
            }
        }

        private void SpawnFounders(long tick)
        {
            int count = (int)_settings.Get(FounderCountSettingId);
            CellCoord center = _mapModule.WorldToCell(_mapModule.WorldWidthMeters * 0.5, _mapModule.WorldDepthMeters * 0.5);
            List<CellCoord> cells = FindFreeCells(center, count, FounderSpacingCells);

            SimRandom random = _randomService.GetStream(RollsStreamName);

            for (int i = 0; i < cells.Count; i++)
            {
                PersonRole role = i == 0 ? PersonRole.Monarch : i == 1 ? PersonRole.Advisor : PersonRole.Founder;
                PersonState state = RollPerson(random, role, cells[i]);
                SpawnPersonWithState(state, tick);
            }
        }

        private EntityId SpawnPersonWithState(PersonState state, long tick)
        {
            EntityId id = _entities.Create(EntityKind.Person);
            _people.Add(id, state);

            CellCoord cell = _mapModule.WorldToCell(state.WorldX, state.WorldZ);
            state.ReservedCell = cell;
            _reservedCells[cell] = id;
            StartIdle(id, state, tick, "Just arrived.");

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
        /// Free cells (not on a tree, stone or mound, and not already held
        /// by another person), nearest to center first, at least
        /// minSpacingCells apart from each other - an expanding ring
        /// search, so the result is fully deterministic (no random rolls
        /// needed to place founders a few meters apart).
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

                    if (!IsCellFreeForPerson(candidate))
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

        #region Actions, movement and idle milling (R10)

        /// <summary>Free of any map placement, and not reserved by a different person; a person's own reservation never blocks themselves.</summary>
        private bool IsCellFreeForPerson(CellCoord cell, EntityId? selfId = null)
        {
            if (!_mapModule.IsCellFree(cell))
            {
                return false;
            }

            return !_reservedCells.TryGetValue(cell, out EntityId owner) || owner == selfId;
        }

        /// <summary>How many meters a Walk action covers in one tick at 1x - a tick is always the same slice of in-game time, so running more ticks per real second (2x, 3x) is the entire "scaled by game speed" effect; this value itself never changes with speed.</summary>
        private double ComputeMetersPerTick()
        {
            double walkSpeedMps = _settings.Get(WalkSpeedMpsSettingId);
            double realSecondsPerDay = _settings.Get(RealSecondsPerDaySettingId);
            if (realSecondsPerDay <= 0)
            {
                return 0;
            }

            double secondsPerTickAt1x = realSecondsPerDay / CalendarConstants.TicksPerDay;
            return walkSpeedMps * secondsPerTickAt1x;
        }

        private void UpdateActions(long tick)
        {
            double metersPerTick = ComputeMetersPerTick();
            double cellSizeMeters = _mapModule.CellSizeMeters;

            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows())
            {
                ProcessPersonAction(row.Key, row.Value, tick, metersPerTick, cellSizeMeters);
            }
        }

        private void ProcessPersonAction(EntityId id, PersonState person, long tick, double metersPerTick, double cellSizeMeters)
        {
            PersonAction action = person.CurrentAction;
            if (action == null)
            {
                StartIdle(id, person, tick, "Taking it all in.");
                return;
            }

            if (action.Kind == ActionKind.Walk)
            {
                AdvanceWalk(person, action, metersPerTick, cellSizeMeters);
            }

            action.CurrentTickForProgress = tick;

            if (!action.HasEnded)
            {
                return;
            }

            long duration = tick - action.StartTick;
            PushHistory(person, new PersonActionHistoryEntry(action.Kind, action.Thought, action.StartTick, duration));

            if (action.Kind == ActionKind.Idle)
            {
                StartWander(id, person, tick);
            }
            else
            {
                StartIdle(id, person, tick, "Resting for a moment.");
            }
        }

        private static void AdvanceWalk(PersonState person, PersonAction action, double metersPerTick, double cellSizeMeters)
        {
            double remaining = metersPerTick;
            while (remaining > 1e-9 && action.NextPathIndex < action.Path.Count)
            {
                CellCoord waypoint = action.Path[action.NextPathIndex];
                double targetX = (waypoint.X + 0.5) * cellSizeMeters;
                double targetZ = (waypoint.Z + 0.5) * cellSizeMeters;
                double dx = targetX - person.WorldX;
                double dz = targetZ - person.WorldZ;
                double distance = Math.Sqrt(dx * dx + dz * dz);

                if (distance <= remaining || distance < 1e-9)
                {
                    person.WorldX = targetX;
                    person.WorldZ = targetZ;
                    action.DistanceTraveledMeters += distance;
                    remaining -= distance;
                    action.NextPathIndex++;
                }
                else
                {
                    double t = remaining / distance;
                    person.WorldX += dx * t;
                    person.WorldZ += dz * t;
                    action.DistanceTraveledMeters += remaining;
                    remaining = 0;
                }
            }
        }

        private void PushHistory(PersonState person, PersonActionHistoryEntry entry)
        {
            int cap = Math.Max(1, (int)_settings.Get(ActionHistoryCountSettingId));
            person.History.Insert(0, entry);
            while (person.History.Count > cap)
            {
                person.History.RemoveAt(person.History.Count - 1);
            }
        }

        private void StartIdle(EntityId id, PersonState person, long tick, string thought)
        {
            SimRandom random = _randomService.GetStream(ActionsStreamName);
            double minSeconds = _settings.Get(IdleWaitMinSecondsSettingId);
            double maxSeconds = _settings.Get(IdleWaitMaxSecondsSettingId);
            double realSecondsPerDay = _settings.Get(RealSecondsPerDaySettingId);

            double waitSeconds = maxSeconds <= minSeconds ? minSeconds : minSeconds + random.NextFloat() * (maxSeconds - minSeconds);
            double ticksPerSecondAt1x = realSecondsPerDay > 0 ? CalendarConstants.TicksPerDay / realSecondsPerDay : 0;
            long durationTicks = Math.Max(1, (long)Math.Round(waitSeconds * ticksPerSecondAt1x));

            person.CurrentAction = new PersonAction
            {
                Kind = ActionKind.Idle,
                Thought = thought,
                StartTick = tick,
                DurationTicks = durationTicks,
                CurrentTickForProgress = tick
            };
        }

        private void StartWander(EntityId id, PersonState person, long tick)
        {
            SimRandom random = _randomService.GetStream(ActionsStreamName);
            double radiusMeters = _settings.Get(IdleWanderRadiusMetersSettingId);
            double cellSize = _mapModule.CellSizeMeters;
            CellCoord current = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            CellCoord? target = TryPickWanderTarget(random, current, radiusMeters, cellSize, id);
            if (!target.HasValue)
            {
                StartIdle(id, person, tick, "Nowhere free to wander right now.");
                return;
            }

            StartWalk(id, person, target.Value, "Nothing to do, stretching my legs.", tick);
        }

        private CellCoord? TryPickWanderTarget(SimRandom random, CellCoord current, double radiusMeters, double cellSizeMeters, EntityId selfId)
        {
            int radiusCells = Math.Max(1, (int)Math.Round(radiusMeters / cellSizeMeters));

            for (int attempt = 0; attempt < WanderTargetAttempts; attempt++)
            {
                int dx = random.NextInt(-radiusCells, radiusCells + 1);
                int dz = random.NextInt(-radiusCells, radiusCells + 1);
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                if (dx * dx + dz * dz > radiusCells * radiusCells)
                {
                    continue;
                }

                var candidate = new CellCoord(current.X + dx, current.Z + dz);
                if (IsCellFreeForPerson(candidate, selfId))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Starts a Walk toward targetCell, reserving it and releasing the
        /// person's old reservation. If the cell isn't free, or no path to
        /// it exists within the pathfinding distance cap, the Walk starts
        /// and ends on the very same tick, with a thought saying why.
        /// </summary>
        private void StartWalk(EntityId id, PersonState person, CellCoord targetCell, string thought, long tick)
        {
            CellCoord startCell = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            if (startCell == targetCell)
            {
                BeginFailedWalk(person, "Already there.", tick);
                return;
            }

            if (!IsCellFreeForPerson(targetCell, id))
            {
                BeginFailedWalk(person, "Someone - or something - is already there.", tick);
                return;
            }

            double maxDistanceMeters = _settings.Get(PathfindingMaxDistanceMetersSettingId);
            double cellSize = _mapModule.CellSizeMeters;

            List<CellCoord> path = CellPathFinder.FindPath(startCell, targetCell, maxDistanceMeters, cellSize, _mapModule.IsCellFree);
            if (path == null || path.Count == 0)
            {
                BeginFailedWalk(person, "Couldn't find a way there.", tick);
                return;
            }

            _reservedCells.Remove(person.ReservedCell);
            _reservedCells[targetCell] = id;
            person.ReservedCell = targetCell;

            double totalDistance = PathLengthMeters(person.WorldX, person.WorldZ, path, cellSize);

            person.CurrentAction = new PersonAction
            {
                Kind = ActionKind.Walk,
                Thought = thought,
                StartTick = tick,
                TargetCell = targetCell,
                Path = path,
                NextPathIndex = 0,
                DistanceTraveledMeters = 0,
                TotalDistanceMeters = totalDistance,
                CurrentTickForProgress = tick
            };
        }

        /// <summary>A Walk that never actually moves - its own Progress is 1 from the moment it's created, so the next action-system tick notices it has ended and picks what comes next.</summary>
        private static void BeginFailedWalk(PersonState person, string failureReason, long tick)
        {
            person.CurrentAction = new PersonAction
            {
                Kind = ActionKind.Walk,
                Thought = failureReason,
                StartTick = tick,
                TargetCell = person.ReservedCell,
                TotalDistanceMeters = 0,
                CurrentTickForProgress = tick
            };
        }

        private static double PathLengthMeters(double startX, double startZ, List<CellCoord> path, double cellSizeMeters)
        {
            double total = 0;
            double prevX = startX;
            double prevZ = startZ;

            foreach (CellCoord cell in path)
            {
                double x = (cell.X + 0.5) * cellSizeMeters;
                double z = (cell.Z + 0.5) * cellSizeMeters;
                double dx = x - prevX;
                double dz = z - prevZ;
                total += Math.Sqrt(dx * dx + dz * dz);
                prevX = x;
                prevZ = z;
            }

            return total;
        }

        /// <summary>The one scheduled system behind every person's action and movement - registered every tick, so walking is never jerky at any speed.</summary>
        private sealed class PersonActionSystem : ISimSystem
        {
            private readonly PeopleModule _module;

            public PersonActionSystem(PeopleModule module)
            {
                _module = module;
            }

            public string Id => ActionSystemId;

            public void Update(long tick) => _module.UpdateActions(tick);
        }

        #endregion

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

                writer.WriteInt(state.ReservedCell.X);
                writer.WriteInt(state.ReservedCell.Z);
                WriteAction(writer, state.CurrentAction);
                WriteHistory(writer, state.History);
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

                var state = new PersonState(unitContentId, worldX, worldZ, name, sex, age, role, specialtyId, skills, attributes);

                var reservedCell = new CellCoord(reader.ReadInt(), reader.ReadInt());
                state.ReservedCell = reservedCell;
                state.CurrentAction = ReadAction(reader);
                state.CurrentAction.CurrentTickForProgress = _context.CurrentTick;
                ReadHistory(reader, state.History);

                _people.Add(id, state);
                _reservedCells[reservedCell] = id;
            }
        }

        private static void WriteAction(SaveWriter writer, PersonAction action)
        {
            writer.WriteInt((int)action.Kind);
            writer.WriteString(action.Thought);
            writer.WriteLong(action.StartTick);
            writer.WriteLong(action.DurationTicks);
            writer.WriteInt(action.TargetCell.X);
            writer.WriteInt(action.TargetCell.Z);
            writer.WriteInt(action.Path.Count);
            foreach (CellCoord cell in action.Path)
            {
                writer.WriteInt(cell.X);
                writer.WriteInt(cell.Z);
            }

            writer.WriteInt(action.NextPathIndex);
            writer.WriteDouble(action.DistanceTraveledMeters);
            writer.WriteDouble(action.TotalDistanceMeters);
        }

        private static PersonAction ReadAction(SaveReader reader)
        {
            var action = new PersonAction
            {
                Kind = (ActionKind)reader.ReadInt(),
                Thought = reader.ReadString(),
                StartTick = reader.ReadLong(),
                DurationTicks = reader.ReadLong(),
                TargetCell = new CellCoord(reader.ReadInt(), reader.ReadInt())
            };

            int pathCount = reader.ReadInt();
            var path = new List<CellCoord>(pathCount);
            for (int i = 0; i < pathCount; i++)
            {
                path.Add(new CellCoord(reader.ReadInt(), reader.ReadInt()));
            }

            action.Path = path;
            action.NextPathIndex = reader.ReadInt();
            action.DistanceTraveledMeters = reader.ReadDouble();
            action.TotalDistanceMeters = reader.ReadDouble();
            return action;
        }

        private static void WriteHistory(SaveWriter writer, List<PersonActionHistoryEntry> history)
        {
            writer.WriteInt(history.Count);
            foreach (PersonActionHistoryEntry entry in history)
            {
                writer.WriteInt((int)entry.Kind);
                writer.WriteString(entry.Thought);
                writer.WriteLong(entry.StartTick);
                writer.WriteLong(entry.DurationTicks);
            }
        }

        private static void ReadHistory(SaveReader reader, List<PersonActionHistoryEntry> history)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var kind = (ActionKind)reader.ReadInt();
                string thought = reader.ReadString();
                long startTick = reader.ReadLong();
                long durationTicks = reader.ReadLong();
                history.Add(new PersonActionHistoryEntry(kind, thought, startTick, durationTicks));
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
