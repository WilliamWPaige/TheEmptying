using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Zones;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// People (R7: entity and world position; R8: identity and stats; R10:
    /// actions, real-time walking, idle milling, thoughts and history;
    /// R11: nobody exists until the kingdom is founded, after which the 9
    /// founders start inside the keep and come out one at a time; R12:
    /// idle people prefer to wander inside a Living Area, if one exists
    /// and they can path to it). Depends on Map (to find a free cell -
    /// not on a tree, stone, mound or building, and not already held by
    /// another person - to stand or walk a person on), Buildings (to
    /// know which cells a building blocks, and to release people from one
    /// at its door) and Zones (to find a Living Area to wander inside).
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

        /// <summary>R11: the shortest the wait between two people leaving the keep can roll, in real seconds at 1x (TEMPORARY placeholder).</summary>
        public const string KeepExitIntervalMinSecondsSettingId = "setting.people.keep_exit_interval_min_seconds";

        /// <summary>R11: the longest the wait between two people leaving the keep can roll, in real seconds at 1x (TEMPORARY placeholder).</summary>
        public const string KeepExitIntervalMaxSecondsSettingId = "setting.people.keep_exit_interval_max_seconds";

        /// <summary>R13: how many game days it takes a full person's hunger to drain to zero (TEMPORARY placeholder).</summary>
        public const string HungerDrainDaysSettingId = "setting.people.hunger_drain_days";

        /// <summary>R13: below this hunger value, a person goes to eat at the next action boundary (TEMPORARY placeholder).</summary>
        public const string HungerEatThresholdSettingId = "setting.people.hunger_eat_threshold";

        /// <summary>R13: how long eating one grub from a stockpile or home stash takes, in game minutes (TEMPORARY placeholder).</summary>
        public const string EatingDurationMinutesSettingId = "setting.people.eating_duration_minutes";

        /// <summary>R13: the home stash grub count below which a person tops it up after eating at a mound (TEMPORARY placeholder).</summary>
        public const string HomeStashMinimumGrubsSettingId = "setting.people.home_stash_minimum_grubs";

        /// <summary>R13: about how many game years of full-time work it takes to go from skill 1 to the cap (TEMPORARY placeholder).</summary>
        public const string SkillYearsToMasterSettingId = "setting.people.skill_years_to_master";

        /// <summary>R13: how many game hours a day counts as "full-time work" for the experience curve (TEMPORARY placeholder).</summary>
        public const string SkillWorkHoursPerDaySettingId = "setting.people.skill_work_hours_per_day";

        /// <summary>R13: how many times longer each skill level takes than the one before it (TEMPORARY placeholder).</summary>
        public const string SkillLevelGrowthFactorSettingId = "setting.people.skill_level_growth_factor";

        /// <summary>R13: how many kinds of work each person rolls as liked at creation (TEMPORARY placeholder).</summary>
        public const string LikedWorkCountSettingId = "setting.people.liked_work_count";

        /// <summary>R13: how many kinds of work each person rolls as disliked at creation (TEMPORARY placeholder).</summary>
        public const string DislikedWorkCountSettingId = "setting.people.disliked_work_count";

        /// <summary>R13: how many times more likely liked work is chosen over an unrated kind (TEMPORARY placeholder).</summary>
        public const string LikedWorkChoiceWeightSettingId = "setting.people.liked_work_choice_weight";

        /// <summary>R13: how many times as likely disliked work is chosen, compared to an unrated kind (TEMPORARY placeholder).</summary>
        public const string DislikedWorkChoiceWeightSettingId = "setting.people.disliked_work_choice_weight";

        /// <summary>R13: how much faster (fraction) experience builds in liked work (TEMPORARY placeholder).</summary>
        public const string LikedWorkExperienceBonusSettingId = "setting.people.liked_work_experience_bonus";

        /// <summary>R13b: the shortest a person waits, after finding no free spot at a resource or a stockpile, before trying again (TEMPORARY placeholder).</summary>
        public const string SpotWaitMinSecondsSettingId = "setting.people.spot_wait_min_seconds";

        /// <summary>R13b: the longest a person waits, after finding no free spot at a resource or a stockpile, before trying again (TEMPORARY placeholder).</summary>
        public const string SpotWaitMaxSecondsSettingId = "setting.people.spot_wait_max_seconds";

        /// <summary>R13b: debug - logs each person's goal, chosen target (and why) and any failure reason every time they decide their next action. On by default in the Editor.</summary>
        public const string LogDecisionsSettingId = "setting.people.log_decisions";

        /// <summary>Owned by time_settings.json, not by any module - the same real-time-to-tick conversion the presentation layer's tick rate uses.</summary>
        private const string RealSecondsPerDaySettingId = "setting.time.real_seconds_per_day";

        private const string RollsStreamName = "people.rolls";
        private const string ActionsStreamName = "people.actions";
        private const string ActionSystemId = "people.actions";

        /// <summary>R14: posted whenever a person digs, carries or eats an item - feeds the discovery system.</summary>
        private const string ItemHeldEventId = "event.people.item_held";

        /// <summary>R14: posted whenever a person finishes harvesting from a resource - feeds the discovery system.</summary>
        private const string ResourceHarvestedEventId = "event.people.resource_harvested";

        /// <summary>R14: posted whenever a person starts a new action - feeds the discovery system.</summary>
        private const string ActionPerformedEventId = "event.people.action_performed";

        /// <summary>The six skills every founder (and the Advisor) is good at by design - not a setting, the design names these specific skills.</summary>
        private static readonly HashSet<string> CoreTradeSkillIds = new HashSet<string>
        {
            "skill.logging", "skill.quarrying", "skill.farming", "skill.foraging", "skill.hauling", "skill.construction"
        };

        private const string LeadershipSkillId = "skill.leadership";
        private const string SpeechSkillId = "skill.speech";

        /// <summary>R13: the one skill digging grubs builds, for food or for stockpile work - not a setting, the design names this specific skill.</summary>
        private const string ForagingSkillId = "skill.foraging";

        /// <summary>R13: the one resource kind hunger and foraging dig from, for now - not a setting, the design names grubs and grub mounds specifically.</summary>
        private const string GrubMoundResourceId = "resource.grub_mound";

        /// <summary>R13: skills stop here - a fixed design fact (not itself a placeholder), matching the existing 1-20 roll ranges.</summary>
        private const int MaxSkillLevel = 20;

        /// <summary>R13: hunger's own maximum - a full person.</summary>
        private const double FullHunger = 100.0;

        /// <summary>Placeholder for "a few meters apart" - cells and meters are the same at the default 1 m cell size.</summary>
        private const int FounderSpacingCells = 3;

        private const int MaxSearchRadiusCells = 256;
        private const int WanderTargetAttempts = 20;

        /// <summary>R13: how many chunk-rings outward to search for the nearest grub mound before giving up.</summary>
        private const int MoundSearchMaxChunkRadius = 8;

        private readonly MapModule _mapModule;
        private readonly BuildingsModule _buildingsModule;
        private readonly ZonesModule _zonesModule;
        private EntityRegistry _entities;
        private SettingsRegistry _settings;
        private RandomService _randomService;
        private ContentDatabase _content;
        private ModuleContext _context;
        private EntityTable<PersonState> _people;

        /// <summary>The one cell each reserving person currently holds - their standing cell while Idle, or the cell they're walking to while Walk. People waiting InsideBuilding hold none. Rebuilt from each person's own ReservedCell on load.</summary>
        private readonly Dictionary<CellCoord, EntityId> _reservedCells = new Dictionary<CellCoord, EntityId>();

        /// <summary>R13b: each home building's own shared stash, by item content Id - for now, every founder's home is the keep, so everyone shares one pantry.</summary>
        private readonly Dictionary<EntityId, Dictionary<string, int>> _homeStashesByBuilding = new Dictionary<EntityId, Dictionary<string, int>>();

        private static readonly Dictionary<string, int> EmptyHomeStash = new Dictionary<string, int>();

        /// <summary>R11: the tick the next person waiting inside a building should try to come out. Meaningless (and never checked) while nobody is inside.</summary>
        private long _nextExitTick;

        /// <summary>R13b: where LogDecision sends its lines, set by the presentation layer; null (the default) means decisions are never logged, regardless of the setting.</summary>
        private Action<string> _decisionLogSink;

        public PeopleModule(MapModule mapModule, BuildingsModule buildingsModule, ZonesModule zonesModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
            _buildingsModule = buildingsModule ?? throw new ArgumentNullException(nameof(buildingsModule));
            _zonesModule = zonesModule ?? throw new ArgumentNullException(nameof(zonesModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId, BuildingsModule.ModuleId, ZonesModule.ModuleId };
        public int SectionVersion => 6;

        /// <summary>Every current person's Id and state, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, PersonState>> People => _people.Rows();

        public bool TryGetPerson(EntityId id, out PersonState person) => _people.TryGet(id, out person);

        /// <summary>R13b: where to send each logged decision line (see LogDecisionsSettingId); set by the presentation layer, which decides how (or whether) to show it.</summary>
        public void SetDecisionLogSink(Action<string> sink) => _decisionLogSink = sink;

        /// <summary>R13b: a home building's own shared stash (everyone living there draws from, and tops up, the same pantry); empty if nothing has ever been stored there.</summary>
        public IReadOnlyDictionary<string, int> GetHomeStash(EntityId buildingId) =>
            _homeStashesByBuilding.TryGetValue(buildingId, out Dictionary<string, int> stash) ? stash : EmptyHomeStash;

        /// <summary>R13b: adds to a home building's shared stash - public so a future trade/delivery system (and tests) can top it up the same way foraging does.</summary>
        public void DepositIntoHomeStash(EntityId buildingId, string itemContentId, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemContentId))
            {
                return;
            }

            if (!_homeStashesByBuilding.TryGetValue(buildingId, out Dictionary<string, int> stash))
            {
                stash = new Dictionary<string, int>();
                _homeStashesByBuilding[buildingId] = stash;
            }

            stash.TryGetValue(itemContentId, out int current);
            stash[itemContentId] = current + amount;
        }

        private void RemoveFromHomeStash(EntityId buildingId, string itemContentId, int amount)
        {
            if (!_homeStashesByBuilding.TryGetValue(buildingId, out Dictionary<string, int> stash) || !stash.TryGetValue(itemContentId, out int current))
            {
                return;
            }

            int remaining = Math.Max(0, current - amount);
            if (remaining <= 0)
            {
                stash.Remove(itemContentId);
            }
            else
            {
                stash[itemContentId] = remaining;
            }
        }

        private int HomeStashGrubCount(PersonState person)
        {
            if (!person.HomeBuildingId.HasValue)
            {
                return 0;
            }

            IReadOnlyDictionary<string, int> stash = GetHomeStash(person.HomeBuildingId.Value);
            return stash.TryGetValue(GrubItemId(), out int count) ? count : 0;
        }

        /// <summary>R13b: logs name, goal, the chosen target (and why) or a failure reason - a no-op unless both a sink is wired up and LogDecisionsSettingId is on.</summary>
        private void LogDecision(PersonState person, string message)
        {
            if (_decisionLogSink == null || _settings.Get(LogDecisionsSettingId) == 0)
            {
                return;
            }

            _decisionLogSink($"{person.Name} ({person.Goal}): {message}");
        }

        /// <summary>R14: a person now holds (dug, carries, or is eating) this item - feeds the discovery system.</summary>
        private void PostItemHeld(string itemContentId)
        {
            _context.Events.Post(ItemHeldEventId, _context.CurrentTick, new EventData().SetString("contentId", itemContentId));
        }

        /// <summary>R14: a person just finished harvesting from this resource - feeds the discovery system.</summary>
        private void PostResourceHarvested(string resourceContentId)
        {
            _context.Events.Post(ResourceHarvestedEventId, _context.CurrentTick, new EventData().SetString("contentId", resourceContentId));
        }

        /// <summary>R14: a person just started a new action of this kind - feeds the discovery system. Any new ActionKind only needs a mapping here and a matching action_definition entry in data; the discovery system itself never changes.</summary>
        private void PostActionPerformed(ActionKind kind)
        {
            _context.Events.Post(ActionPerformedEventId, _context.CurrentTick, new EventData().SetString("contentId", ActionContentId(kind)));
        }

        private static string ActionContentId(ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.Idle: return "action.idle";
                case ActionKind.Walk: return "action.walk";
                case ActionKind.InsideBuilding: return "action.inside_building";
                case ActionKind.Eating: return "action.eating";
                case ActionKind.Digging: return "action.digging";
                default: return "action.unknown";
            }
        }

        /// <summary>R13: the live hunger-eat-threshold value, for the info window's hunger bar color.</summary>
        public double HungerEatThreshold => _settings.Get(HungerEatThresholdSettingId);

        /// <summary>R13: this skill's current accumulated hours toward its next level, and how many hours that level needs - for the info window's progress bar. (0, 0) once the skill is already at the cap.</summary>
        public (double AccumulatedHours, double HoursNeeded) GetSkillProgress(PersonState person, string skillId)
        {
            int level = person.Skills.TryGetValue(skillId, out int lvl) ? lvl : 1;
            if (level >= MaxSkillLevel)
            {
                return (0.0, 0.0);
            }

            double accumulated = person.SkillExperienceHours.TryGetValue(skillId, out double hours) ? hours : 0.0;

            double yearsToMaster = _settings.Get(SkillYearsToMasterSettingId);
            double workHoursPerDay = _settings.Get(SkillWorkHoursPerDaySettingId);
            double growthFactor = _settings.Get(SkillLevelGrowthFactorSettingId);
            double daysPerYear = CalendarConstants.DaysPerMonth * CalendarConstants.MonthsPerYear;
            double baseHours = SkillProgressionMath.BaseHoursForFirstLevel(yearsToMaster, workHoursPerDay, daysPerYear, growthFactor, MaxSkillLevel);
            double hoursNeeded = SkillProgressionMath.HoursForLevel(level, baseHours, growthFactor);

            return (accumulated, hoursNeeded);
        }

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _settings = context.Settings;
            _randomService = context.RandomService;
            _content = context.Content;
            _context = context;
            _people = new EntityTable<PersonState>(_entities);

            context.RegisterSystem(new PersonActionSystem(this), interval: 1, offset: 0, order: 0);

            // R11: a new game starts with nobody on the map - people exist only once the kingdom is founded (see SpawnFoundersInside), whether this is a new game or a load.
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

        public void RemovePerson(EntityId id)
        {
            if (_people.TryGet(id, out PersonState state))
            {
                _reservedCells.Remove(state.ReservedCell);
                _entities.Destroy(id);
            }
        }

        /// <summary>
        /// R11: founding only. Rolls the 9 founders (Monarch, Advisor, then
        /// Founders - exactly as R8 always has) and creates them all
        /// waiting inside the given building, not drawn, with the thought
        /// "Waiting to head out." Starts the keep-exit timer so they come
        /// out one at a time afterward.
        /// </summary>
        public void SpawnFoundersInside(EntityId buildingId)
        {
            if (!_buildingsModule.TryGetBuilding(buildingId, out BuildingState building))
            {
                return;
            }

            long tick = _context.CurrentTick;
            int count = (int)_settings.Get(FounderCountSettingId);
            SimRandom random = _randomService.GetStream(RollsStreamName);

            for (int i = 0; i < count; i++)
            {
                PersonRole role = i == 0 ? PersonRole.Monarch : i == 1 ? PersonRole.Advisor : PersonRole.Founder;
                PersonState state = RollPerson(random, role, building.DoorCell);
                state.ReservedCell = building.DoorCell;
                state.HomeBuildingId = buildingId;
                state.CurrentAction = new PersonAction
                {
                    Kind = ActionKind.InsideBuilding,
                    Thought = "Waiting to head out.",
                    StartTick = tick,
                    BuildingId = buildingId,
                    CurrentTickForProgress = tick
                };

                EntityId id = _entities.Create(EntityKind.Person);
                _people.Add(id, state);
                PostActionPerformed(ActionKind.InsideBuilding);
            }

            RollNextExitTick(tick);
        }

        /// <summary>Every person currently waiting inside the given building, in ascending entity order - for the keep's info window.</summary>
        public IEnumerable<PersonState> GetPeopleInside(EntityId buildingId)
        {
            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows())
            {
                PersonAction action = row.Value.CurrentAction;
                if (action != null && action.Kind == ActionKind.InsideBuilding && action.BuildingId == buildingId)
                {
                    yield return row.Value;
                }
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

            var state = new PersonState(PersonUnitContentId, worldX, worldZ, name, sex, age, role, specialtyId, skillValues, attributeValues);

            (List<string> likes, List<string> dislikes) = RollLikesAndDislikes(random, skills);
            state.LikedSkillIds.AddRange(likes);
            state.DislikedSkillIds.AddRange(dislikes);

            return state;
        }

        /// <summary>R13: rolls liked and disliked kinds of work from the full skills list - never the same skill in both, from the same roll stream as the rest of this person's identity.</summary>
        private (List<string> Likes, List<string> Dislikes) RollLikesAndDislikes(SimRandom random, List<SkillDefinition> skills)
        {
            var pool = skills.Select(s => s.Id).ToList();

            int likedCount = Math.Max(0, Math.Min((int)_settings.Get(LikedWorkCountSettingId), pool.Count));
            var likes = new List<string>();
            for (int i = 0; i < likedCount; i++)
            {
                int index = random.NextInt(0, pool.Count);
                likes.Add(pool[index]);
                pool.RemoveAt(index);
            }

            int dislikedCount = Math.Max(0, Math.Min((int)_settings.Get(DislikedWorkCountSettingId), pool.Count));
            var dislikes = new List<string>();
            for (int i = 0; i < dislikedCount; i++)
            {
                int index = random.NextInt(0, pool.Count);
                dislikes.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return (likes, dislikes);
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

        /// <summary>Free of any map placement and of any building's footprint (its door cell excepted), and not reserved by a different person; a person's own reservation never blocks themselves.</summary>
        private bool IsCellFreeForPerson(CellCoord cell, EntityId? selfId = null)
        {
            if (!_mapModule.IsCellFree(cell) || _buildingsModule.IsCellBlocked(cell))
            {
                return false;
            }

            return !_reservedCells.TryGetValue(cell, out EntityId owner) || owner == selfId;
        }

        /// <summary>What a path may step on: free of map placements and of any building's blocking cells (its door cell excepted) - never blocked by another person, who may be passed through.</summary>
        private bool IsWalkable(CellCoord cell)
        {
            return _mapModule.IsCellFree(cell) && !_buildingsModule.IsCellBlocked(cell);
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

            UpdateKeepExits(tick);
        }

        /// <summary>
        /// R11: once due, releases the first person still waiting inside a
        /// building (ascending entity order - Monarch, Advisor, then
        /// Founders), as long as that building's door cell is currently
        /// free; otherwise tries again next tick at the same due time. A
        /// fresh interval is rolled only once someone actually comes out.
        /// </summary>
        private void UpdateKeepExits(long tick)
        {
            if (tick < _nextExitTick)
            {
                return;
            }

            EntityId waitingId = default;
            PersonState waitingPerson = null;
            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows())
            {
                if (row.Value.CurrentAction?.Kind == ActionKind.InsideBuilding)
                {
                    waitingId = row.Key;
                    waitingPerson = row.Value;
                    break;
                }
            }

            if (waitingPerson == null)
            {
                return;
            }

            if (!_buildingsModule.TryGetBuilding(waitingPerson.CurrentAction.BuildingId, out BuildingState building) ||
                !IsCellFreeForPerson(building.DoorCell, waitingId))
            {
                return;
            }

            PersonAction insideAction = waitingPerson.CurrentAction;
            PushHistory(waitingPerson, new PersonActionHistoryEntry(insideAction.Kind, insideAction.Thought, insideAction.StartTick, tick - insideAction.StartTick));

            _reservedCells[building.DoorCell] = waitingId;
            waitingPerson.ReservedCell = building.DoorCell;
            StartIdle(waitingId, waitingPerson, tick, "Outside at last.");

            RollNextExitTick(tick);
        }

        private void RollNextExitTick(long tick)
        {
            SimRandom random = _randomService.GetStream(ActionsStreamName);
            double minSeconds = _settings.Get(KeepExitIntervalMinSecondsSettingId);
            double maxSeconds = _settings.Get(KeepExitIntervalMaxSecondsSettingId);
            double realSecondsPerDay = _settings.Get(RealSecondsPerDaySettingId);

            double intervalSeconds = maxSeconds <= minSeconds ? minSeconds : minSeconds + random.NextFloat() * (maxSeconds - minSeconds);
            double ticksPerSecondAt1x = realSecondsPerDay > 0 ? CalendarConstants.TicksPerDay / realSecondsPerDay : 0;
            long intervalTicks = Math.Max(1, (long)Math.Round(intervalSeconds * ticksPerSecondAt1x));

            _nextExitTick = tick + intervalTicks;
        }

        private void ProcessPersonAction(EntityId id, PersonState person, long tick, double metersPerTick, double cellSizeMeters)
        {
            PersonAction action = person.CurrentAction;
            if (action == null)
            {
                StartIdle(id, person, tick, "Taking it all in.");
                return;
            }

            if (action.Kind != ActionKind.InsideBuilding)
            {
                ApplyHungerDrain(person);
            }

            bool alreadySeekingFood = person.Goal == PersonGoal.SeekingFood || person.Goal == PersonGoal.ToppingUpHomeStash;
            if (person.Hunger <= 0.0 && action.Kind != ActionKind.InsideBuilding && !alreadySeekingFood)
            {
                long interruptedDuration = tick - action.StartTick;
                PushHistory(person, new PersonActionHistoryEntry(action.Kind, action.Thought, action.StartTick, interruptedDuration));
                GrantExperienceIfApplicable(person, action, interruptedDuration, tick);
                DecideNextAction(id, person, tick);
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
            GrantExperienceIfApplicable(person, action, duration, tick);
            ApplyActionCompletionEffects(person, action);

            DecideNextAction(id, person, tick);
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
            StartTimedIdle(person, tick, thought, IdleWaitMinSecondsSettingId, IdleWaitMaxSecondsSettingId);
        }

        /// <summary>R13b: when a spot (a mound slot, a stockpile cell) is taken everywhere this person looked - wait a short while, with no stored "what I was trying," then try the exact same goal again. Never retries the same failed spot immediately in a loop.</summary>
        private void StartWaitingForSpot(EntityId id, PersonState person, long tick)
        {
            StartTimedIdle(person, tick, "Waiting for a spot to free up.", SpotWaitMinSecondsSettingId, SpotWaitMaxSecondsSettingId);
        }

        private void StartTimedIdle(PersonState person, long tick, string thought, string minSecondsSettingId, string maxSecondsSettingId)
        {
            SimRandom random = _randomService.GetStream(ActionsStreamName);
            double minSeconds = _settings.Get(minSecondsSettingId);
            double maxSeconds = _settings.Get(maxSecondsSettingId);
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

            PostActionPerformed(ActionKind.Idle);
        }

        /// <summary>R12: while at least one Living Area exists, idle people prefer to wander inside the nearest one they can actually path to; with none (or none reachable), they wander nearby as before.</summary>
        private void StartWander(EntityId id, PersonState person, long tick)
        {
            SimRandom random = _randomService.GetStream(ActionsStreamName);
            double cellSize = _mapModule.CellSizeMeters;
            CellCoord current = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            CellCoord? target = TryPickTargetInNearestReachableLivingArea(random, person, current, cellSize, id);

            if (!target.HasValue)
            {
                double radiusMeters = _settings.Get(IdleWanderRadiusMetersSettingId);
                target = TryPickWanderTarget(random, current, radiusMeters, cellSize, id);
            }

            if (!target.HasValue)
            {
                StartIdle(id, person, tick, "Nowhere free to wander right now.");
                return;
            }

            StartWalk(id, person, target.Value, "Nothing to do, stretching my legs.", tick);
        }

        private CellCoord? TryPickTargetInNearestReachableLivingArea(SimRandom random, PersonState person, CellCoord startCell, double cellSize, EntityId selfId)
        {
            List<ZoneState> livingAreas = _zonesModule.LivingAreas
                .Select(r => r.Value)
                .OrderBy(z => DistanceSquared(person.WorldX, person.WorldZ, z.CenterX, z.CenterZ))
                .ToList();

            if (livingAreas.Count == 0)
            {
                return null;
            }

            double maxDistanceMeters = _settings.Get(PathfindingMaxDistanceMetersSettingId);

            foreach (ZoneState area in livingAreas)
            {
                CellCoord? candidate = TryPickCellInZone(random, area, cellSize, selfId);
                if (!candidate.HasValue)
                {
                    continue;
                }

                List<CellCoord> path = CellPathFinder.FindPath(startCell, candidate.Value, maxDistanceMeters, cellSize, IsWalkable);
                if (path != null && path.Count > 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        private CellCoord? TryPickCellInZone(SimRandom random, ZoneState area, double cellSizeMeters, EntityId selfId)
        {
            for (int attempt = 0; attempt < WanderTargetAttempts; attempt++)
            {
                double localA = (random.NextFloat() - 0.5f) * area.SideALength;
                double localB = (random.NextFloat() - 0.5f) * area.SideBLength;
                double rad = area.RotationDegrees * Math.PI / 180.0;
                double worldX = area.CenterX + localA * Math.Cos(rad) - localB * Math.Sin(rad);
                double worldZ = area.CenterZ + localA * Math.Sin(rad) + localB * Math.Cos(rad);

                CellCoord candidate = _mapModule.WorldToCell(worldX, worldZ);
                if (IsCellFreeForPerson(candidate, selfId))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static double DistanceSquared(double x1, double z1, double x2, double z2)
        {
            double dx = x1 - x2;
            double dz = z1 - z2;
            return dx * dx + dz * dz;
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
                if (IsCellFreeForPerson(candidate, selfId) && !IsInsideAnyStockpile(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>R13b: stockpiles are drop-off and pick-up only - an idle wander target never lands inside one.</summary>
        private bool IsInsideAnyStockpile(CellCoord cell)
        {
            double cellSize = _mapModule.CellSizeMeters;
            double worldX = (cell.X + 0.5) * cellSize;
            double worldZ = (cell.Z + 0.5) * cellSize;

            foreach (KeyValuePair<EntityId, ZoneState> row in _zonesModule.Stockpiles)
            {
                if (row.Value.Rect.Contains(worldX, worldZ))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Hunger, foraging, experience and likes (R13)

        private void ApplyHungerDrain(PersonState person)
        {
            double drainDays = _settings.Get(HungerDrainDaysSettingId);
            if (drainDays <= 0)
            {
                return;
            }

            double drainPerTick = FullHunger / (drainDays * CalendarConstants.TicksPerDay);
            person.Hunger = Math.Max(0.0, person.Hunger - drainPerTick);
        }

        private void GrantExperienceIfApplicable(PersonState person, PersonAction action, long durationTicks, long tick)
        {
            if (string.IsNullOrEmpty(action.SkillId) || durationTicks <= 0)
            {
                return;
            }

            double hours = durationTicks / (double)CalendarConstants.TicksPerHour;
            if (person.LikedSkillIds.Contains(action.SkillId))
            {
                hours *= 1.0 + _settings.Get(LikedWorkExperienceBonusSettingId);
            }

            double accumulated = person.SkillExperienceHours.TryGetValue(action.SkillId, out double existing) ? existing : 0.0;
            accumulated += hours;

            int currentLevel = person.Skills.TryGetValue(action.SkillId, out int level) ? level : 1;

            double yearsToMaster = _settings.Get(SkillYearsToMasterSettingId);
            double workHoursPerDay = _settings.Get(SkillWorkHoursPerDaySettingId);
            double growthFactor = _settings.Get(SkillLevelGrowthFactorSettingId);
            double daysPerYear = CalendarConstants.DaysPerMonth * CalendarConstants.MonthsPerYear;
            double baseHours = SkillProgressionMath.BaseHoursForFirstLevel(yearsToMaster, workHoursPerDay, daysPerYear, growthFactor, MaxSkillLevel);

            while (currentLevel < MaxSkillLevel)
            {
                double hoursNeeded = SkillProgressionMath.HoursForLevel(currentLevel, baseHours, growthFactor);
                if (hoursNeeded <= 0 || accumulated < hoursNeeded)
                {
                    break;
                }

                accumulated -= hoursNeeded;
                currentLevel++;
                person.SetSkillLevel(action.SkillId, currentLevel);
                PushNote(person, $"{DisplayNameForSkill(action.SkillId)} rose to {currentLevel}.", tick);
            }

            person.SkillExperienceHours[action.SkillId] = accumulated;
        }

        private void PushNote(PersonState person, string text, long tick)
        {
            int cap = Math.Max(1, (int)_settings.Get(ActionHistoryCountSettingId));
            person.Notes.Insert(0, new PersonNote(text, tick));
            while (person.Notes.Count > cap)
            {
                person.Notes.RemoveAt(person.Notes.Count - 1);
            }
        }

        private string DisplayNameForSkill(string skillId)
        {
            if (_content.TryGet(BuiltInContentTypes.SkillDefinitionTypeName, skillId, out ContentEntry entry) && entry.Definition is SkillDefinition skill)
            {
                return skill.DisplayName;
            }

            return skillId;
        }

        /// <summary>Applies the result of a just-finished (not interrupted) Eating or Digging action - which grub pile shrinks, whose hunger rises, or what gets added to carried goods, depending on the goal it served.</summary>
        private void ApplyActionCompletionEffects(PersonState person, PersonAction action)
        {
            string grubItemId = GrubItemId();

            if (action.Kind == ActionKind.Eating)
            {
                PostItemHeld(grubItemId);

                if (person.TargetIsHome && person.HomeBuildingId.HasValue)
                {
                    RemoveFromHomeStash(person.HomeBuildingId.Value, grubItemId, 1);
                }
                else if (person.TargetZoneId.HasValue)
                {
                    _zonesModule.RemoveItemFromStockpile(person.TargetZoneId.Value, grubItemId, 1);
                }

                person.Hunger = Math.Min(FullHunger, person.Hunger + ItemFoodValue(grubItemId));
                person.LastAteFromMound = false;
                return;
            }

            if (action.Kind == ActionKind.Digging)
            {
                PostResourceHarvested(GrubMoundResourceId);
                PostItemHeld(grubItemId);

                if (person.Goal == PersonGoal.SeekingFood)
                {
                    person.Hunger = Math.Min(FullHunger, person.Hunger + ItemFoodValue(grubItemId));
                    person.LastAteFromMound = true;
                }
                else if (person.Goal == PersonGoal.ToppingUpHomeStash || person.Goal == PersonGoal.ForagingForStockpile)
                {
                    person.CarriedItemId = grubItemId;
                    person.CarriedAmount += 1;
                }
            }
        }

        /// <summary>
        /// R13: re-decided from scratch every time an action ends (or
        /// hunger forces an early stop) - never a stored "what step am I
        /// on," so an interrupted trip (hunger, mainly) always resumes
        /// correctly from wherever the person actually ended up.
        /// </summary>
        private void DecideNextAction(EntityId id, PersonState person, long tick)
        {
            double eatThreshold = _settings.Get(HungerEatThresholdSettingId);
            bool alreadySeekingFood = person.Goal == PersonGoal.SeekingFood || person.Goal == PersonGoal.ToppingUpHomeStash;

            if (!alreadySeekingFood && person.Hunger < eatThreshold)
            {
                person.Goal = PersonGoal.SeekingFood;
            }

            switch (person.Goal)
            {
                case PersonGoal.SeekingFood:
                    DecideSeekingFood(id, person, tick);
                    return;

                case PersonGoal.ToppingUpHomeStash:
                    DecideToppingUpHomeStash(id, person, tick);
                    return;

                case PersonGoal.ForagingForStockpile:
                    DecideForagingForStockpile(id, person, tick);
                    return;

                default:
                    if (person.CarriedAmount > 0)
                    {
                        person.Goal = PersonGoal.ForagingForStockpile;
                        DecideNextAction(id, person, tick);
                        return;
                    }

                    if (ForagingIsAvailable() && ChooseToForageInsteadOfIdle(person))
                    {
                        person.Goal = PersonGoal.ForagingForStockpile;
                        DecideNextAction(id, person, tick);
                        return;
                    }

                    CellCoord standingCell = _mapModule.WorldToCell(person.WorldX, person.WorldZ);
                    if (IsInsideAnyStockpile(standingCell))
                    {
                        StepOutOfStockpileThenIdleOrWander(id, person, tick);
                        return;
                    }

                    if (person.CurrentAction != null && person.CurrentAction.Kind == ActionKind.Idle)
                    {
                        StartWander(id, person, tick);
                    }
                    else
                    {
                        StartIdle(id, person, tick, "Resting for a moment.");
                    }

                    return;
            }
        }

        /// <summary>How far StepOutOfStockpileThenIdleOrWander will search, ring by ring, for a free cell outside every stockpile - generous, since a stockpile is never more than a few cells across.</summary>
        private const int StockpileExitSearchMaxRadiusCells = 12;

        /// <summary>R13b: stockpiles are drop-off and pick-up only - a person who just delivered or ate there steps out to a free, non-stockpile cell before idling or wandering, instead of standing inside it.</summary>
        private void StepOutOfStockpileThenIdleOrWander(EntityId id, PersonState person, long tick)
        {
            CellCoord current = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            for (int radius = 1; radius <= StockpileExitSearchMaxRadiusCells; radius++)
            {
                foreach (CellCoord candidate in RingCells(current, radius))
                {
                    if (IsCellFreeForPerson(candidate, id) && !IsInsideAnyStockpile(candidate))
                    {
                        LogDecision(person, $"stepping out of the stockpile to {candidate}.");
                        StartWalk(id, person, candidate, "Stepping out of the stockpile.", tick);
                        return;
                    }
                }
            }

            LogDecision(person, "boxed in by the stockpile everywhere nearby - waiting for a spot to free up.");
            StartWaitingForSpot(id, person, tick);
        }

        private void DecideSeekingFood(EntityId id, PersonState person, long tick)
        {
            if (person.Hunger >= FullHunger)
            {
                int homeStashMinimum = (int)_settings.Get(HomeStashMinimumGrubsSettingId);
                if (person.LastAteFromMound && HomeStashGrubCount(person) < homeStashMinimum)
                {
                    LogDecision(person, "full after eating at a mound, and the home stash is low - topping it up.");
                    person.Goal = PersonGoal.ToppingUpHomeStash;
                    DecideNextAction(id, person, tick);
                    return;
                }

                LogDecision(person, "full - back to whatever comes next.");
                person.Goal = PersonGoal.None;
                DecideNextAction(id, person, tick);
                return;
            }

            CellCoord currentCell = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            if (person.TargetIsHome && IsAtHome(person, currentCell))
            {
                LogDecision(person, "eating from the home stash.");
                StartEatingAction(id, person, tick, "Eating from the home stash.");
                return;
            }

            if (!person.TargetIsHome && person.TargetZoneId.HasValue && IsInsideZone(currentCell, person.TargetZoneId.Value))
            {
                LogDecision(person, "eating from the stockpile.");
                StartEatingAction(id, person, tick, "Eating from the stockpile.");
                return;
            }

            if (person.TargetIsMound && IsAdjacentToMound(currentCell))
            {
                LogDecision(person, "digging up a grub to eat (adjacent to mound).");
                StartDiggingAction(id, person, tick, "Digging up a grub to eat.");
                return;
            }

            if (!TryFindNearestFoodSource(id, person, currentCell, out CellCoord approachCell, out EntityId? zoneId, out bool isHome, out bool isMound))
            {
                LogDecision(person, "hungry, but no free food source right now - waiting for a spot to free up.");
                StartWaitingForSpot(id, person, tick);
                return;
            }

            person.TargetZoneId = zoneId;
            person.TargetIsHome = isHome;
            person.TargetIsMound = isMound;

            bool starving = person.Hunger <= 0.0;
            string thought = isHome
                ? (starving ? "Starving - rushing home to eat." : "Heading home to eat.")
                : isMound
                    ? (starving ? "Starving - digging up anything to eat." : "Off to dig up something to eat.")
                    : (starving ? "Starving - rushing to the stockpile." : "Off to the stockpile to find something to eat.");
            LogDecision(person, $"heading to {(isHome ? "home" : isMound ? "a mound" : "a stockpile")} ({approachCell}) to eat - {thought}");
            StartWalk(id, person, approachCell, thought, tick);
        }

        private void DecideToppingUpHomeStash(EntityId id, PersonState person, long tick)
        {
            string grubItemId = GrubItemId();
            int carryCapacity = ItemCarryAmount(grubItemId);
            CellCoord currentCell = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            if (person.CarriedAmount < carryCapacity)
            {
                if (IsAdjacentToMound(currentCell))
                {
                    LogDecision(person, "digging extra grubs to take home (adjacent to mound).");
                    StartDiggingAction(id, person, tick, "Digging extra grubs to take home.");
                    return;
                }

                double maxDistanceMeters = _settings.Get(PathfindingMaxDistanceMetersSettingId);
                if (TryFindNearestReachableMound(id, currentCell, maxDistanceMeters, out CellCoord moundApproach))
                {
                    LogDecision(person, $"walking to mound approach {moundApproach} to dig extra grubs for home.");
                    StartWalk(id, person, moundApproach, "Off to dig extra grubs to take home.", tick);
                    return;
                }

                LogDecision(person, "no free mound spot to top up the home stash - waiting for a spot to free up.");
                StartWaitingForSpot(id, person, tick);
                return;
            }

            if (!person.HomeBuildingId.HasValue || !_buildingsModule.TryGetBuilding(person.HomeBuildingId.Value, out BuildingState home))
            {
                LogDecision(person, "no home to take grubs to - giving up on topping up.");
                person.Goal = PersonGoal.None;
                DecideNextAction(id, person, tick);
                return;
            }

            if (currentCell == home.DoorCell)
            {
                LogDecision(person, $"depositing {person.CarriedAmount} {person.CarriedItemId} into the home stash.");
                DepositIntoHomeStash(person.HomeBuildingId.Value, person.CarriedItemId, person.CarriedAmount);
                person.CarriedAmount = 0;
                person.CarriedItemId = string.Empty;
                person.Goal = PersonGoal.None;
                DecideNextAction(id, person, tick);
                return;
            }

            LogDecision(person, $"walking home ({home.DoorCell}) to drop off {person.CarriedAmount} {person.CarriedItemId}.");
            StartWalk(id, person, home.DoorCell, "Taking grubs home.", tick);
        }

        private void DecideForagingForStockpile(EntityId id, PersonState person, long tick)
        {
            string grubItemId = GrubItemId();
            int carryCapacity = ItemCarryAmount(grubItemId);
            double maxDistanceMeters = _settings.Get(PathfindingMaxDistanceMetersSettingId);
            CellCoord currentCell = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            bool haveValidTarget = person.ForagingDeliveryZoneId.HasValue && _zonesModule.TryGetZone(person.ForagingDeliveryZoneId.Value, out ZoneState _);
            if (!haveValidTarget)
            {
                if (!TryFindNearestStockpileWithRoom(id, currentCell, grubItemId, maxDistanceMeters, out EntityId zoneId, out CellCoord _))
                {
                    if (person.CarriedAmount > 0 && TryDeliverLeftoverGoodsHome(id, person, currentCell, tick))
                    {
                        return;
                    }

                    LogDecision(person, "no stockpile needs grubs right now.");
                    person.Goal = PersonGoal.None;
                    StartIdle(id, person, tick, person.CarriedAmount > 0 ? "Nowhere to take this for now." : "No stockpile needs grubs right now.");
                    return;
                }

                person.ForagingDeliveryZoneId = zoneId;
                LogDecision(person, $"chose stockpile {zoneId} as the delivery target.");
            }

            EntityId targetZoneId = person.ForagingDeliveryZoneId.Value;
            int remainingRoom = _zonesModule.GetRemainingRoomForItem(targetZoneId, grubItemId);
            int stillWantToDig = Math.Max(0, Math.Min(carryCapacity - person.CarriedAmount, remainingRoom));

            if (stillWantToDig > 0)
            {
                if (IsAdjacentToMound(currentCell))
                {
                    LogDecision(person, "digging grubs for the stockpile (adjacent to mound).");
                    StartDiggingAction(id, person, tick, "Digging grubs for the stockpile.");
                    return;
                }

                if (TryFindNearestReachableMound(id, currentCell, maxDistanceMeters, out CellCoord moundApproach))
                {
                    LogDecision(person, $"walking to mound approach {moundApproach} to dig grubs for the stockpile.");
                    StartWalk(id, person, moundApproach, "Off to dig grubs for the stockpile.", tick);
                    return;
                }

                if (person.CarriedAmount <= 0)
                {
                    LogDecision(person, "no free mound spot to forage - waiting for a spot to free up.");
                    StartWaitingForSpot(id, person, tick);
                    return;
                }

                // No mound reachable, but carrying something already - deliver what we have instead of giving up entirely.
            }

            if (IsInsideZone(currentCell, targetZoneId))
            {
                int amountBeforeDelivery = person.CarriedAmount;
                DeliverCarriedGoods(person, targetZoneId);
                LogDecision(person, $"delivered {amountBeforeDelivery - person.CarriedAmount} {grubItemId} to the stockpile.");
                person.ForagingDeliveryZoneId = null;
                person.Goal = PersonGoal.None;
                DecideNextAction(id, person, tick);
                return;
            }

            if (!_zonesModule.TryGetZone(targetZoneId, out ZoneState targetZone))
            {
                LogDecision(person, "the stockpile is gone - giving up this trip.");
                person.ForagingDeliveryZoneId = null;
                person.Goal = PersonGoal.None;
                StartIdle(id, person, tick, "The stockpile is gone.");
                return;
            }

            CellCoord? deliverCell = TryPickCellInZone(_randomService.GetStream(ActionsStreamName), targetZone, _mapModule.CellSizeMeters, id);
            if (!deliverCell.HasValue)
            {
                LogDecision(person, "no free cell at the stockpile - waiting for a spot to free up.");
                person.ForagingDeliveryZoneId = null;
                StartWaitingForSpot(id, person, tick);
                return;
            }

            LogDecision(person, $"walking to stockpile cell {deliverCell.Value} to drop off {person.CarriedAmount} {grubItemId}.");
            StartWalk(id, person, deliverCell.Value, "Carrying grubs to the stockpile.", tick);
        }

        /// <summary>R13b: goods never vanish - when no stockpile has room for what's carried, walk it home and add it to the shared stash instead of holding it indefinitely.</summary>
        private bool TryDeliverLeftoverGoodsHome(EntityId id, PersonState person, CellCoord currentCell, long tick)
        {
            if (!person.HomeBuildingId.HasValue || !_buildingsModule.TryGetBuilding(person.HomeBuildingId.Value, out BuildingState home))
            {
                return false;
            }

            if (currentCell == home.DoorCell)
            {
                LogDecision(person, $"no stockpile has room - depositing {person.CarriedAmount} {person.CarriedItemId} at home instead.");
                DepositIntoHomeStash(person.HomeBuildingId.Value, person.CarriedItemId, person.CarriedAmount);
                person.CarriedAmount = 0;
                person.CarriedItemId = string.Empty;
                person.Goal = PersonGoal.None;
                DecideNextAction(id, person, tick);
                return true;
            }

            LogDecision(person, $"no stockpile has room - taking {person.CarriedAmount} {person.CarriedItemId} home instead.");
            StartWalk(id, person, home.DoorCell, "No stockpile has room - taking it home instead.", tick);
            return true;
        }

        private bool ForagingIsAvailable()
        {
            string grubItemId = GrubItemId();
            foreach (KeyValuePair<EntityId, ZoneState> row in _zonesModule.Stockpiles)
            {
                (int amount, int limit, bool blocked) = _zonesModule.GetStockpileItemInfo(row.Key, grubItemId);
                if (!blocked && amount < limit)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>R13: weighs foraging (the only kind of work there is, so far) against idling by this person's like/dislike of Foraging specifically.</summary>
        private bool ChooseToForageInsteadOfIdle(PersonState person)
        {
            double forageWeight = 1.0;
            if (person.LikedSkillIds.Contains(ForagingSkillId))
            {
                forageWeight = _settings.Get(LikedWorkChoiceWeightSettingId);
            }
            else if (person.DislikedSkillIds.Contains(ForagingSkillId))
            {
                forageWeight = _settings.Get(DislikedWorkChoiceWeightSettingId);
            }

            double pForage = forageWeight / (forageWeight + 1.0);
            SimRandom random = _randomService.GetStream(ActionsStreamName);
            return random.Chance((float)pForage);
        }

        private bool TryFindNearestFoodSource(EntityId selfId, PersonState person, CellCoord startCell, out CellCoord approachCell, out EntityId? zoneId, out bool isHome, out bool isMound)
        {
            approachCell = default;
            zoneId = null;
            isHome = false;
            isMound = false;

            double maxDistanceMeters = _settings.Get(PathfindingMaxDistanceMetersSettingId);
            double cellSize = _mapModule.CellSizeMeters;
            string grubItemId = GrubItemId();
            SimRandom random = _randomService.GetStream(ActionsStreamName);

            double startWorldX = (startCell.X + 0.5) * cellSize;
            double startWorldZ = (startCell.Z + 0.5) * cellSize;

            var candidates = new List<(double DistanceSq, CellCoord Approach, EntityId? ZoneId, bool IsHome, bool IsMound)>();

            foreach (KeyValuePair<EntityId, ZoneState> row in _zonesModule.Stockpiles)
            {
                (int amount, _, bool blocked) = _zonesModule.GetStockpileItemInfo(row.Key, grubItemId);
                if (amount <= 0 || blocked)
                {
                    continue;
                }

                CellCoord? cell = TryPickCellInZone(random, row.Value, cellSize, selfId);
                if (!cell.HasValue)
                {
                    continue;
                }

                candidates.Add((DistanceSquared(startWorldX, startWorldZ, row.Value.CenterX, row.Value.CenterZ), cell.Value, row.Key, false, false));
            }

            if (person.HomeBuildingId.HasValue && HomeStashGrubCount(person) > 0 &&
                _buildingsModule.TryGetBuilding(person.HomeBuildingId.Value, out BuildingState homeBuilding))
            {
                double homeWorldX = (homeBuilding.DoorCell.X + 0.5) * cellSize;
                double homeWorldZ = (homeBuilding.DoorCell.Z + 0.5) * cellSize;
                candidates.Add((DistanceSquared(startWorldX, startWorldZ, homeWorldX, homeWorldZ), homeBuilding.DoorCell, null, true, false));
            }

            if (TryFindNearestReachableMound(selfId, startCell, maxDistanceMeters, out CellCoord moundApproach))
            {
                double moundWorldX = (moundApproach.X + 0.5) * cellSize;
                double moundWorldZ = (moundApproach.Z + 0.5) * cellSize;
                candidates.Add((DistanceSquared(startWorldX, startWorldZ, moundWorldX, moundWorldZ), moundApproach, null, false, true));
            }

            foreach (var candidate in candidates.OrderBy(c => c.DistanceSq))
            {
                List<CellCoord> path = CellPathFinder.FindPath(startCell, candidate.Approach, maxDistanceMeters, cellSize, IsWalkable);
                if (path == null)
                {
                    continue;
                }

                approachCell = candidate.Approach;
                zoneId = candidate.ZoneId;
                isHome = candidate.IsHome;
                isMound = candidate.IsMound;
                return true;
            }

            return false;
        }

        private bool TryFindNearestStockpileWithRoom(EntityId selfId, CellCoord startCell, string itemContentId, double maxDistanceMeters, out EntityId zoneId, out CellCoord approachCell)
        {
            zoneId = default;
            approachCell = default;

            double cellSize = _mapModule.CellSizeMeters;
            double startWorldX = (startCell.X + 0.5) * cellSize;
            double startWorldZ = (startCell.Z + 0.5) * cellSize;
            SimRandom random = _randomService.GetStream(ActionsStreamName);

            List<KeyValuePair<EntityId, ZoneState>> ordered = _zonesModule.Stockpiles
                .Where(row => _zonesModule.GetRemainingRoomForItem(row.Key, itemContentId) > 0)
                .OrderBy(row => DistanceSquared(startWorldX, startWorldZ, row.Value.CenterX, row.Value.CenterZ))
                .ToList();

            foreach (KeyValuePair<EntityId, ZoneState> row in ordered)
            {
                CellCoord? cell = TryPickCellInZone(random, row.Value, cellSize, selfId);
                if (!cell.HasValue)
                {
                    continue;
                }

                List<CellCoord> path = CellPathFinder.FindPath(startCell, cell.Value, maxDistanceMeters, cellSize, IsWalkable);
                if (path == null)
                {
                    continue;
                }

                zoneId = row.Key;
                approachCell = cell.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The nearest grub mound with a free approach cell this person could
        /// reserve, within its own max-workers limit - another mound, nearer
        /// or farther, is tried next whenever one is already at that limit or
        /// has no walkable neighbor left free. Never the same contested cell
        /// twice: a mound at its limit is skipped outright, not retried.
        /// </summary>
        private bool TryFindNearestReachableMound(EntityId selfId, CellCoord startCell, double maxDistanceMeters, out CellCoord approachCell)
        {
            approachCell = default;
            ChunkCoord startChunk = _mapModule.CellToChunk(startCell);
            double cellSize = _mapModule.CellSizeMeters;
            int maxWorkers = Math.Max(1, GrubMoundResourceContent()?.MaxWorkers ?? 1);

            var candidates = new List<(double DistanceSq, CellCoord Approach)>();

            for (int radius = 0; radius <= MoundSearchMaxChunkRadius; radius++)
            {
                foreach (ChunkCoord chunkCoord in ChunkRing(startChunk, radius))
                {
                    ChunkData chunk = _mapModule.GetOrCreateChunk(chunkCoord);
                    foreach (ChunkPlacement placement in chunk.Placements)
                    {
                        if (placement.Kind != PlacementKind.GrubMound)
                        {
                            continue;
                        }

                        CellCoord? approach = TryFindApproachCellAdjacentTo(placement.Cell, selfId, maxWorkers);
                        if (!approach.HasValue)
                        {
                            continue;
                        }

                        double worldX = (approach.Value.X + 0.5) * cellSize;
                        double worldZ = (approach.Value.Z + 0.5) * cellSize;
                        double startWorldX = (startCell.X + 0.5) * cellSize;
                        double startWorldZ = (startCell.Z + 0.5) * cellSize;
                        candidates.Add((DistanceSquared(startWorldX, startWorldZ, worldX, worldZ), approach.Value));
                    }
                }
            }

            foreach (var candidate in candidates.OrderBy(c => c.DistanceSq))
            {
                List<CellCoord> path = CellPathFinder.FindPath(startCell, candidate.Approach, maxDistanceMeters, cellSize, IsWalkable);
                if (path == null)
                {
                    continue;
                }

                approachCell = candidate.Approach;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A free walkable neighbor of this mound this person could reserve -
        /// null if the mound already has maxWorkers people reserving one of
        /// its neighbor cells (treated as unavailable), even if some other
        /// neighbor happens to still be free. A neighbor this person already
        /// reserves counts as free to them, never as one of "the others".
        /// </summary>
        private CellCoord? TryFindApproachCellAdjacentTo(CellCoord moundCell, EntityId selfId, int maxWorkers)
        {
            CellCoord? firstFree = null;
            int reservedByOthers = 0;

            foreach (CellCoord neighbor in Neighbors8(moundCell))
            {
                if (!IsWalkable(neighbor))
                {
                    continue;
                }

                if (_reservedCells.TryGetValue(neighbor, out EntityId owner) && owner != selfId)
                {
                    reservedByOthers++;
                    continue;
                }

                if (!firstFree.HasValue)
                {
                    firstFree = neighbor;
                }
            }

            return reservedByOthers >= maxWorkers ? null : firstFree;
        }

        private bool IsAdjacentToMound(CellCoord cell)
        {
            foreach (CellCoord neighbor in Neighbors8(cell))
            {
                ChunkData chunk = _mapModule.GetOrCreateChunk(_mapModule.CellToChunk(neighbor));
                foreach (ChunkPlacement placement in chunk.Placements)
                {
                    if (placement.Kind == PlacementKind.GrubMound && placement.Cell == neighbor)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsAtHome(PersonState person, CellCoord currentCell)
        {
            return person.HomeBuildingId.HasValue
                   && _buildingsModule.TryGetBuilding(person.HomeBuildingId.Value, out BuildingState building)
                   && currentCell == building.DoorCell;
        }

        private bool IsInsideZone(CellCoord cell, EntityId zoneId)
        {
            if (!_zonesModule.TryGetZone(zoneId, out ZoneState zone))
            {
                return false;
            }

            double cellSize = _mapModule.CellSizeMeters;
            double worldX = (cell.X + 0.5) * cellSize;
            double worldZ = (cell.Z + 0.5) * cellSize;
            return zone.Rect.Contains(worldX, worldZ);
        }

        private static IEnumerable<CellCoord> Neighbors8(CellCoord cell)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    yield return new CellCoord(cell.X + dx, cell.Z + dz);
                }
            }
        }

        private static IEnumerable<ChunkCoord> ChunkRing(ChunkCoord center, int radius)
        {
            if (radius == 0)
            {
                yield return center;
                yield break;
            }

            for (int dx = -radius; dx <= radius; dx++)
            {
                yield return new ChunkCoord(center.X + dx, center.Z - radius);
                yield return new ChunkCoord(center.X + dx, center.Z + radius);
            }

            for (int dz = -radius + 1; dz <= radius - 1; dz++)
            {
                yield return new ChunkCoord(center.X - radius, center.Z + dz);
                yield return new ChunkCoord(center.X + radius, center.Z + dz);
            }
        }

        private void StartEatingAction(EntityId id, PersonState person, long tick, string thought)
        {
            double minutes = _settings.Get(EatingDurationMinutesSettingId);
            long durationTicks = Math.Max(1, (long)Math.Round(minutes * CalendarConstants.TicksPerMinute));

            person.CurrentAction = new PersonAction
            {
                Kind = ActionKind.Eating,
                Thought = thought,
                StartTick = tick,
                DurationTicks = durationTicks,
                CurrentTickForProgress = tick
            };

            PostActionPerformed(ActionKind.Eating);
        }

        private void StartDiggingAction(EntityId id, PersonState person, long tick, string thought)
        {
            ResourceContent mound = GrubMoundResourceContent();
            int skillLevel = person.Skills.TryGetValue(ForagingSkillId, out int level) ? level : 1;

            double minutes = mound != null
                ? HarvestTimeBlend.BlendMinutes(skillLevel, mound.HarvestTimeUnskilledMinutes, mound.HarvestTimeSkilledMinutes, mound.HarvestTimeMasterMinutes)
                : 60.0;

            long durationTicks = Math.Max(1, (long)Math.Round(minutes * CalendarConstants.TicksPerMinute));

            person.CurrentAction = new PersonAction
            {
                Kind = ActionKind.Digging,
                Thought = thought,
                StartTick = tick,
                DurationTicks = durationTicks,
                SkillId = ForagingSkillId,
                CurrentTickForProgress = tick
            };

            PostActionPerformed(ActionKind.Digging);
        }

        /// <summary>R13b: goods never vanish - only what the stockpile actually had room for is removed from what's carried; any leftover stays carried, for the caller to take elsewhere.</summary>
        private void DeliverCarriedGoods(PersonState person, EntityId zoneId)
        {
            if (person.CarriedAmount <= 0 || string.IsNullOrEmpty(person.CarriedItemId))
            {
                return;
            }

            (int added, int _, string _) = _zonesModule.TryAddItemToStockpile(zoneId, person.CarriedItemId, person.CarriedAmount);
            person.CarriedAmount -= added;
            if (person.CarriedAmount <= 0)
            {
                person.CarriedItemId = string.Empty;
            }
        }

        private ResourceContent GrubMoundResourceContent()
        {
            if (_content.TryGet(BuiltInContentTypes.ResourceTypeName, GrubMoundResourceId, out ContentEntry entry) && entry.Definition is ResourceContent resource)
            {
                return resource;
            }

            return null;
        }

        private string GrubItemId()
        {
            ResourceContent resource = GrubMoundResourceContent();
            return resource != null && !resource.YieldedItem.IsEmpty ? resource.YieldedItem.Id : "item.grub";
        }

        private double ItemFoodValue(string itemContentId)
        {
            if (_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) && entry.Definition is ItemContent item)
            {
                return item.FoodValue;
            }

            return 0.0;
        }

        private int ItemCarryAmount(string itemContentId)
        {
            if (_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) && entry.Definition is ItemContent item)
            {
                return Math.Max(1, item.CarryAmount);
            }

            return 1;
        }

        #endregion

        #region Movement (R10)

        /// <summary>
        /// Starts a Walk toward targetCell, reserving it and releasing the
        /// person's old reservation. If the cell isn't free, or no path to
        /// it exists within the pathfinding distance cap, the Walk starts
        /// and ends on the very same tick, with a thought saying why.
        /// </summary>
        private void StartWalk(EntityId id, PersonState person, CellCoord targetCell, string thought, long tick)
        {
            PostActionPerformed(ActionKind.Walk);

            CellCoord startCell = _mapModule.WorldToCell(person.WorldX, person.WorldZ);

            if (startCell == targetCell)
            {
                BeginFailedWalk(person, "Already there.", tick);
                return;
            }

            if (!IsCellFreeForPerson(targetCell, id))
            {
                LogDecision(person, $"walk to {targetCell} failed - someone or something is already there.");
                BeginFailedWalk(person, "Someone - or something - is already there.", tick);
                return;
            }

            double maxDistanceMeters = _settings.Get(PathfindingMaxDistanceMetersSettingId);
            double cellSize = _mapModule.CellSizeMeters;

            List<CellCoord> path = CellPathFinder.FindPath(startCell, targetCell, maxDistanceMeters, cellSize, IsWalkable);
            if (path == null || path.Count == 0)
            {
                LogDecision(person, $"walk to {targetCell} failed - no path within range.");
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
            writer.WriteLong(_nextExitTick);

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
                WriteNotes(writer, state.Notes);

                writer.WriteDouble(state.Hunger);
                writer.WriteString(state.CarriedItemId);
                writer.WriteInt(state.CarriedAmount);
                writer.WriteEntityId(state.HomeBuildingId ?? EntityId.None);
                writer.WriteInt((int)state.Goal);
                writer.WriteEntityId(state.TargetZoneId ?? EntityId.None);
                writer.WriteEntityId(state.ForagingDeliveryZoneId ?? EntityId.None);
                writer.WriteBool(state.TargetIsHome);
                writer.WriteBool(state.TargetIsMound);
                writer.WriteBool(state.LastAteFromMound);
                writer.WriteStringList(state.LikedSkillIds);
                writer.WriteStringList(state.DislikedSkillIds);
                WriteStringDoubleMap(writer, state.SkillExperienceHours);
            }

            writer.WriteInt(_homeStashesByBuilding.Count);
            foreach (KeyValuePair<EntityId, Dictionary<string, int>> row in _homeStashesByBuilding)
            {
                writer.WriteEntityId(row.Key);
                WriteStringIntMap(writer, row.Value);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            _nextExitTick = reader.ReadLong();

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
                ReadNotes(reader, state.Notes);

                state.Hunger = reader.ReadDouble();
                state.CarriedItemId = reader.ReadString();
                state.CarriedAmount = reader.ReadInt();
                EntityId homeBuildingRaw = reader.ReadEntityId();
                state.HomeBuildingId = homeBuildingRaw.IsNone ? (EntityId?)null : homeBuildingRaw;
                state.Goal = (PersonGoal)reader.ReadInt();
                EntityId targetZoneRaw = reader.ReadEntityId();
                state.TargetZoneId = targetZoneRaw.IsNone ? (EntityId?)null : targetZoneRaw;
                EntityId foragingDeliveryZoneRaw = reader.ReadEntityId();
                state.ForagingDeliveryZoneId = foragingDeliveryZoneRaw.IsNone ? (EntityId?)null : foragingDeliveryZoneRaw;
                state.TargetIsHome = reader.ReadBool();
                state.TargetIsMound = reader.ReadBool();
                state.LastAteFromMound = reader.ReadBool();
                state.LikedSkillIds.AddRange(reader.ReadStringList());
                state.DislikedSkillIds.AddRange(reader.ReadStringList());
                foreach (KeyValuePair<string, double> kvp in ReadStringDoubleMap(reader))
                {
                    state.SkillExperienceHours[kvp.Key] = kvp.Value;
                }

                _people.Add(id, state);

                // A person waiting InsideBuilding holds no real reservation (see SpawnFoundersInside) - several of them share the same ReservedCell (the door), so registering it here for every one of them would hand the door's reservation to whichever was read last, blocking everyone else from ever coming out.
                if (state.CurrentAction.Kind != ActionKind.InsideBuilding)
                {
                    _reservedCells[reservedCell] = id;
                }
            }

            int stashCount = reader.ReadInt();
            for (int i = 0; i < stashCount; i++)
            {
                EntityId buildingId = reader.ReadEntityId();
                _homeStashesByBuilding[buildingId] = ReadStringIntMap(reader);
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
            writer.WriteEntityId(action.BuildingId);
            writer.WriteString(action.SkillId);
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
            action.BuildingId = reader.ReadEntityId();
            action.SkillId = reader.ReadString();
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

        private static void WriteNotes(SaveWriter writer, List<PersonNote> notes)
        {
            writer.WriteInt(notes.Count);
            foreach (PersonNote note in notes)
            {
                writer.WriteString(note.Text);
                writer.WriteLong(note.Tick);
            }
        }

        private static void ReadNotes(SaveReader reader, List<PersonNote> notes)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                string text = reader.ReadString();
                long tick = reader.ReadLong();
                notes.Add(new PersonNote(text, tick));
            }
        }

        private static void WriteStringDoubleMap(SaveWriter writer, IReadOnlyDictionary<string, double> map)
        {
            writer.WriteInt(map.Count);
            foreach (KeyValuePair<string, double> kvp in map)
            {
                writer.WriteString(kvp.Key);
                writer.WriteDouble(kvp.Value);
            }
        }

        private static Dictionary<string, double> ReadStringDoubleMap(SaveReader reader)
        {
            int count = reader.ReadInt();
            var map = new Dictionary<string, double>(count);
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                double value = reader.ReadDouble();
                map[key] = value;
            }

            return map;
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
