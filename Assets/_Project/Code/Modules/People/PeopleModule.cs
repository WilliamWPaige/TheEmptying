using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.MapMovement;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// A person is an entity and a mover: name, age and lifespan, health,
    /// needs, mood, skills and a simple daily schedule. Depends on map and
    /// movement (every person is a mover) and inventory (people carry food).
    /// </summary>
    public sealed class PeopleModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.people";

        public const string LifespanMinYearsSettingId = "setting.people.lifespan_min_years";
        public const string LifespanMaxYearsSettingId = "setting.people.lifespan_max_years";
        public const string AdultAgeYearsSettingId = "setting.people.adult_age_years";
        public const string OldAgeYearsSettingId = "setting.people.old_age_years";
        public const string DaysPerLifeYearSettingId = "setting.people.days_per_life_year";
        public const string LifestyleShiftMaxYearsPerMonthSettingId = "setting.people.lifestyle_shift_max_years_per_month";
        public const string OldAgeDeathChancePerYearPastLifespanSettingId = "setting.people.old_age_death_chance_per_year_past_lifespan";
        public const string OldAgeDeathChanceCapSettingId = "setting.people.old_age_death_chance_cap";
        public const string StarvationHoursBeforeDeathSettingId = "setting.people.starvation_hours_before_death";
        public const string HomelessHealthDamagePerNightSettingId = "setting.people.homeless_health_damage_per_night";
        public const string BelieverChanceSettingId = "setting.people.believer_chance";
        public const string NeedLowThresholdSettingId = "setting.people.need_low_threshold_percent";
        public const string NeedCriticalThresholdSettingId = "setting.people.need_critical_threshold_percent";
        public const string BadMoodThresholdSettingId = "setting.people.bad_mood_threshold";
        public const string HomelessMoodPenaltySettingId = "setting.people.homeless_mood_penalty";
        public const string NightStartHourSettingId = "setting.people.night_start_hour";
        public const string NightEndHourSettingId = "setting.people.night_end_hour";
        public const string EatAtHungerPercentSettingId = "setting.people.eat_at_hunger_percent";
        public const string HungerRestoredPerLoafSettingId = "setting.people.hunger_restored_per_loaf";
        public const string SleepRestRestorePerHourSettingId = "setting.people.sleep_rest_restore_per_hour";
        public const string WanderRadiusCellsSettingId = "setting.people.wander_radius_cells";
        public const string WanderChancePerHourSettingId = "setting.people.wander_chance_per_hour";
        public const string StartingImmigrantCountSettingId = "setting.people.starting_immigrant_count";
        public const string StartingImmigrantMinAgeSettingId = "setting.people.starting_immigrant_min_age";
        public const string StartingImmigrantMaxAgeSettingId = "setting.people.starting_immigrant_max_age";
        public const string StartingProvisionsLoavesSettingId = "setting.people.starting_provisions_loaves";

        public const string PersonDiedEventId = "event.people.person_died";
        public const string NeedLowEventId = "event.people.need_low";
        public const string NeedCriticalEventId = "event.people.need_critical";
        public const string NeedRecoveredEventId = "event.people.need_recovered";

        /// <summary>
        /// People has no declared dependency on Wealth (Wealth depends on
        /// People, not the other way), so this is the event's string ID, not
        /// WealthModule.ClassChangedEventId - events are the one channel a
        /// module can listen on without a code reference.
        /// </summary>
        private const string ClassChangedEventId = "event.wealth.class_changed";

        private const string FoodGoodId = "good.loaf";

        private sealed class HourlySystem : ISimSystem
        {
            private readonly PeopleModule _owner;
            public HourlySystem(PeopleModule owner) => _owner = owner;
            public string Id => ModuleId + ".hourly";
            public void Update(long tick) => _owner.UpdateHourly(tick);
        }

        private readonly MapMovementModule _mapMovement;
        private readonly InventoryModule _inventory;

        private EntityTable<PersonState> _people;
        private SettingsRegistry _settings;
        private EventBus _events;
        private EntityRegistry _entities;
        private SimRandom _random;

        private Dictionary<string, Need> _needsById;
        private List<string> _firstNames = new List<string>();
        private List<string> _familyNames = new List<string>();

        /// <summary>A person's social class. Defaults to Lower (correct for brand-new people) until Wealth registers the real one.</summary>
        private Func<EntityId, SocialClass> _classProvider = _ => SocialClass.Lower;

        /// <summary>A housed person's home cell; null (the default, meaning homeless) until Buildings registers the real one.</summary>
        private Func<EntityId, GridPoint?> _homeLocationProvider = _ => null;

        /// <summary>A working person's workplace cell; null (the default, meaning no job) until Jobs registers the real one.</summary>
        private Func<EntityId, GridPoint?> _workplaceLocationProvider = _ => null;

        /// <summary>Whether a person should be at work this hour; false (the default) until Jobs registers the real one.</summary>
        private Func<EntityId, int, bool> _isOnShiftProvider = (id, hour) => false;

        public PeopleModule(MapMovementModule mapMovement, InventoryModule inventory)
        {
            _mapMovement = mapMovement ?? throw new ArgumentNullException(nameof(mapMovement));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapMovementModule.ModuleId, InventoryModule.ModuleId };
        public int SectionVersion => 1;

        /// <summary>Every current person's Id and state, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, PersonState>> People => _people.Rows();

        public bool TryGetPerson(EntityId id, out PersonState person) => _people.TryGet(id, out person);

        /// <summary>Wealth calls this during its own Initialize, since it's the module that actually computes class.</summary>
        public void SetClassProvider(Func<EntityId, SocialClass> provider) => _classProvider = provider ?? (_ => SocialClass.Lower);

        /// <summary>Buildings calls this during its own Initialize, since it's the module that actually tracks who lives where.</summary>
        public void SetHomeLocationProvider(Func<EntityId, GridPoint?> provider) => _homeLocationProvider = provider ?? (_ => null);

        /// <summary>Jobs calls this during its own Initialize, since it's the module that actually tracks who works where and when.</summary>
        public void SetWorkplaceProvider(Func<EntityId, GridPoint?> location, Func<EntityId, int, bool> isOnShift)
        {
            _workplaceLocationProvider = location ?? (_ => null);
            _isOnShiftProvider = isOnShift ?? ((id, hour) => false);
        }

        public GridPoint? GetHomeLocation(EntityId person) => _homeLocationProvider(person);

        /// <summary>Adds fractional progress toward a skill's next level, applying whole levels (capped at the max) as progress crosses 1.</summary>
        public void AddSkillExperience(EntityId person, Skill skill, double amount)
        {
            if (amount <= 0 || !_people.TryGet(person, out PersonState state))
            {
                return;
            }

            int index = (int)skill;
            state.SkillProgress[index] += amount;

            while (state.SkillProgress[index] >= 1.0 && state.Skills[index] < WorkSpeed.MaxSkillLevel)
            {
                state.SkillProgress[index] -= 1.0;
                state.Skills[index]++;
            }

            if (state.Skills[index] >= WorkSpeed.MaxSkillLevel)
            {
                state.SkillProgress[index] = 0.0;
            }
        }

        /// <summary>Jobs calls this once for every hour a person spends working a hard-labor recipe, feeding the lifestyle-lifespan hook.</summary>
        public void RecordHardLaborHour(EntityId person)
        {
            if (_people.TryGet(person, out PersonState state))
            {
                state.HardLaborHoursThisMonth++;
            }
        }

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _entities = context.Entities;
            _random = context.Random;
            _people = new EntityTable<PersonState>(context.Entities);

            LoadContent(context.Content);

            context.Subscribe<NewMonthEvent>(OnNewMonth);
            context.Subscribe(MapMovementModule.MoverArrivedEventId, OnMoverArrived);
            context.Subscribe(ClassChangedEventId, OnClassChanged);
            context.RegisterSystem(new HourlySystem(this), CalendarConstants.TicksPerHour, 0, 0);

            RegisterCommands(context);

            if (!context.IsLoading)
            {
                SpawnStartingImmigrants();
            }
        }

        public void Shutdown()
        {
        }

        #region Content

        private void LoadContent(ContentDatabase content)
        {
            _needsById = new Dictionary<string, Need>();
            foreach (ContentEntry entry in content.ListByType(BuiltInContentTypes.NeedTypeName))
            {
                var need = (Need)entry.Definition;
                _needsById[need.Id] = need;
            }

            foreach (ContentEntry entry in content.ListByType(BuiltInContentTypes.NameListTypeName))
            {
                var nameList = (NameList)entry.Definition;
                List<string> target = nameList.Kind == NameListKind.FirstName ? _firstNames : _familyNames;
                target.AddRange(nameList.Names);
            }
        }

        public bool IsHomeless(EntityId id) => _homeLocationProvider(id) == null;

        #endregion

        #region New game setup

        private void SpawnStartingImmigrants()
        {
            int count = (int)_settings.Get(StartingImmigrantCountSettingId);
            int minAge = (int)_settings.Get(StartingImmigrantMinAgeSettingId);
            int maxAge = (int)_settings.Get(StartingImmigrantMaxAgeSettingId);
            int provisions = (int)_settings.Get(StartingProvisionsLoavesSettingId);

            MapGrid grid = _mapMovement.Grid;
            var center = new GridPoint(grid.Width / 2, grid.Depth / 2);

            for (int i = 0; i < count; i++)
            {
                GridPoint? cell = FindNearbyPassableCell(center, 5) ?? grid.RandomPassableCell(_random);
                if (cell == null)
                {
                    continue; // no passable cell exists at all; nothing more we can do
                }

                EntityId id = _entities.Create(EntityKind.Person);
                _mapMovement.MakeMover(id, cell.Value);

                PersonState person = CreatePerson(minAge, maxAge);
                _people.Add(id, person);
                SyncNeedsToClass(id, person);

                _inventory.Add(id, FoodGoodId, provisions);
            }
        }

        /// <summary>Spawns one more person at a random passable cell, ages minAge to maxAge, with the usual starting provisions. For benchmarks and tests; not used by new-game setup, which goes through SpawnStartingImmigrants instead.</summary>
        public EntityId SpawnPerson(int minAge, int maxAge)
        {
            GridPoint? cell = _mapMovement.Grid.RandomPassableCell(_random);
            if (cell == null)
            {
                return EntityId.None;
            }

            EntityId id = _entities.Create(EntityKind.Person);
            _mapMovement.MakeMover(id, cell.Value);

            PersonState person = CreatePerson(minAge, maxAge);
            _people.Add(id, person);
            SyncNeedsToClass(id, person);

            int provisions = (int)_settings.Get(StartingProvisionsLoavesSettingId);
            _inventory.Add(id, FoodGoodId, provisions);

            return id;
        }

        private PersonState CreatePerson(int minAge, int maxAge)
        {
            var person = new PersonState
            {
                FirstName = PickRandomName(_firstNames),
                FamilyName = PickRandomName(_familyNames),
                AgeLifeYears = _random.NextInt(minAge, maxAge + 1),
                LifespanYears = _random.NextInt((int)_settings.Get(LifespanMinYearsSettingId), (int)_settings.Get(LifespanMaxYearsSettingId) + 1),
                IsBeliever = _random.Chance((float)_settings.Get(BelieverChanceSettingId))
            };

            return person;
        }

        /// <summary>
        /// Adds any need this person's current class (and believer status)
        /// makes applicable but they don't have yet, and removes any need
        /// they have that no longer applies - called at spawn (everything
        /// starts empty, so it only adds) and again whenever Wealth reports a
        /// class change (so a drop loses middle/upper needs, not just a rise
        /// gaining them).
        /// </summary>
        private void SyncNeedsToClass(EntityId id, PersonState person)
        {
            SocialClass personClass = _classProvider(id);

            foreach (Need need in _needsById.Values)
            {
                if (need.Kind != NeedKind.Meter)
                {
                    continue; // state needs (shelter, safety, prestige) aren't tracked as meters
                }

                bool applies = need.MinimumClass <= personClass && (!need.BelieversOnly || person.IsBeliever);
                bool has = person.Needs.ContainsKey(need.Id);

                if (applies && !has)
                {
                    person.Needs[need.Id] = new PersonNeedState();
                }
                else if (!applies && has)
                {
                    person.Needs.Remove(need.Id);
                }
            }
        }

        private void OnClassChanged(PostedEvent posted)
        {
            EntityId id = posted.Data.GetEntity("person");
            if (_people.TryGet(id, out PersonState person))
            {
                SyncNeedsToClass(id, person);
            }
        }

        private string PickRandomName(List<string> names)
        {
            return names.Count == 0 ? "Unnamed" : names[_random.NextInt(0, names.Count)];
        }

        #endregion

        #region Hourly update

        private void UpdateHourly(long tick)
        {
            GameDate date = GameDate.FromTick(tick);
            int nightStartHour = (int)_settings.Get(NightStartHourSettingId);
            int nightEndHour = (int)_settings.Get(NightEndHourSettingId);
            bool isNight = IsNightHour(date.Hour, nightStartHour, nightEndHour);

            float lowThreshold = (float)_settings.Get(NeedLowThresholdSettingId);
            float criticalThreshold = (float)_settings.Get(NeedCriticalThresholdSettingId);
            float badMoodThreshold = (float)_settings.Get(BadMoodThresholdSettingId);
            float homelessPenalty = (float)_settings.Get(HomelessMoodPenaltySettingId);
            float eatAtPercent = (float)_settings.Get(EatAtHungerPercentSettingId);
            float hungerRestorePerLoaf = (float)_settings.Get(HungerRestoredPerLoafSettingId);
            float sleepRestorePerHour = (float)_settings.Get(SleepRestRestorePerHourSettingId);
            double starvationHoursBeforeDeath = _settings.Get(StarvationHoursBeforeDeathSettingId);
            double homelessHealthDamage = _settings.Get(HomelessHealthDamagePerNightSettingId);
            double daysPerLifeYear = _settings.Get(DaysPerLifeYearSettingId);
            float wanderChance = (float)_settings.Get(WanderChancePerHourSettingId);
            int wanderRadius = (int)_settings.Get(WanderRadiusCellsSettingId);

            var toKill = new List<(EntityId Id, string Cause)>();

            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows().ToList())
            {
                EntityId id = row.Key;
                PersonState person = row.Value;

                person.AgeLifeYears += 1.0 / (24.0 * daysPerLifeYear);

                foreach (KeyValuePair<string, PersonNeedState> needRow in person.Needs)
                {
                    Need definition = _needsById[needRow.Key];
                    PersonNeedState needState = needRow.Value;

                    float drainPerHour = 100f / definition.HoursToEmpty;
                    needState.Meter = Math.Max(0f, needState.Meter - drainPerHour);

                    UpdateThreshold(id, needRow.Key, needState, lowThreshold, criticalThreshold, tick);

                    if (needState.Threshold == NeedThresholdState.Critical)
                    {
                        person.HoursHungerCriticalThisMonth += needRow.Key == "need.hunger" ? 1 : 0;
                    }
                }

                bool homeless = IsHomeless(id);
                if (homeless && date.Hour == nightStartHour)
                {
                    person.Health = Math.Max(0.0, person.Health - homelessHealthDamage);
                    person.HomelessNightsThisMonth++;
                }

                person.HoursHungerEmpty = person.Needs.TryGetValue("need.hunger", out PersonNeedState hunger) && hunger.Meter <= 0f
                    ? person.HoursHungerEmpty + 1
                    : 0;

                ChooseActivity(id, person, date.Hour, isNight, lowThreshold, eatAtPercent, hungerRestorePerLoaf, sleepRestorePerHour, wanderChance, wanderRadius);

                person.Mood = ComputeMood(person, homeless, homelessPenalty);

                person.HoursTrackedThisMonth++;
                if (person.Mood < badMoodThreshold)
                {
                    person.HoursBadMoodThisMonth++;
                }

                if (person.Needs.TryGetValue("need.hygiene", out PersonNeedState hygiene))
                {
                    person.HygieneSumThisMonth += hygiene.Meter;
                    person.HygieneSamplesThisMonth++;
                }

                if (person.HoursHungerEmpty >= starvationHoursBeforeDeath)
                {
                    toKill.Add((id, "starvation"));
                }
                else if (person.Health <= 0.0)
                {
                    toKill.Add((id, "poor_health"));
                }
            }

            foreach ((EntityId id, string cause) in toKill)
            {
                KillPerson(id, cause, tick);
            }
        }

        private static bool IsNightHour(int hour, int nightStartHour, int nightEndHour)
        {
            return nightStartHour > nightEndHour
                ? hour >= nightStartHour || hour < nightEndHour
                : hour >= nightStartHour && hour < nightEndHour;
        }

        private void UpdateThreshold(EntityId id, string needId, PersonNeedState needState, float lowThreshold, float criticalThreshold, long tick)
        {
            NeedThresholdState newState = needState.Meter <= criticalThreshold ? NeedThresholdState.Critical
                : needState.Meter <= lowThreshold ? NeedThresholdState.Low
                : NeedThresholdState.Normal;

            if (newState == needState.Threshold)
            {
                return;
            }

            string eventId = newState switch
            {
                NeedThresholdState.Critical => NeedCriticalEventId,
                NeedThresholdState.Low => NeedLowEventId,
                _ => NeedRecoveredEventId
            };

            _events.Post(eventId, tick, new EventData().SetEntity("person", id).SetString("need", needId));
            needState.Threshold = newState;
        }

        private static double ComputeMood(PersonState person, bool homeless, float homelessPenalty)
        {
            if (person.Needs.Count == 0)
            {
                return 100.0;
            }

            double sum = 0.0;
            foreach (KeyValuePair<string, PersonNeedState> kvp in person.Needs)
            {
                sum += kvp.Value.Meter;
            }

            double average = sum / person.Needs.Count;
            if (homeless)
            {
                average -= homelessPenalty;
            }

            return Math.Clamp(average, 0.0, 100.0);
        }

        private void ChooseActivity(EntityId id, PersonState person, int hour, bool isNight, float lowThreshold, float eatAtPercent, float hungerRestorePerLoaf, float sleepRestorePerHour, float wanderChance, int wanderRadius)
        {
            bool isHungry = person.Needs.TryGetValue("need.hunger", out PersonNeedState hunger) && hunger.Meter <= eatAtPercent;
            if (isHungry && _inventory.TryRemove(id, FoodGoodId, 1))
            {
                hunger.Meter = Math.Min(100f, hunger.Meter + hungerRestorePerLoaf);
                person.Activity = PersonActivity.Eating;
                return;
            }

            if (!isHungry && _isOnShiftProvider(id, hour))
            {
                GridPoint? workplace = _workplaceLocationProvider(id);
                if (workplace.HasValue)
                {
                    if (_mapMovement.GetCell(id) != workplace.Value)
                    {
                        if (person.Activity != PersonActivity.Commuting || !_mapMovement.IsMover(id))
                        {
                            _mapMovement.TryMoveTo(id, workplace.Value);
                        }

                        person.Activity = PersonActivity.Commuting;
                    }
                    else
                    {
                        person.Activity = PersonActivity.Working;
                    }

                    return;
                }
            }

            bool isTired = person.Needs.TryGetValue("need.rest", out PersonNeedState rest) && rest.Meter <= lowThreshold;
            if (isTired || isNight)
            {
                GridPoint? home = _homeLocationProvider(id);
                if (home.HasValue && _mapMovement.GetCell(id) != home.Value)
                {
                    if (person.Activity != PersonActivity.GoingHome || !_mapMovement.IsMover(id))
                    {
                        _mapMovement.TryMoveTo(id, home.Value);
                    }

                    person.Activity = PersonActivity.GoingHome;
                    return;
                }

                if (person.Activity != PersonActivity.Sleeping)
                {
                    _mapMovement.Stop(id);
                }

                person.Activity = PersonActivity.Sleeping;
                if (rest != null)
                {
                    rest.Meter = Math.Min(100f, rest.Meter + sleepRestorePerHour);
                }

                return;
            }

            if (person.Activity == PersonActivity.Wandering && _mapMovement.IsMover(id))
            {
                return; // still en route; the arrival event resets this to Idle
            }

            if (_random.Chance(wanderChance))
            {
                GridPoint currentCell = _mapMovement.GetCell(id);
                GridPoint? target = FindNearbyPassableCell(currentCell, wanderRadius);
                if (target != null && _mapMovement.TryMoveTo(id, target.Value))
                {
                    person.Activity = PersonActivity.Wandering;
                    return;
                }
            }

            person.Activity = PersonActivity.Idle;
        }

        /// <summary>Tries a handful of random cells within radius of center, falling back to null (not every map/position has a free cell nearby).</summary>
        private GridPoint? FindNearbyPassableCell(GridPoint center, int radius)
        {
            MapGrid grid = _mapMovement.Grid;
            const int attempts = 12;

            for (int i = 0; i < attempts; i++)
            {
                int dx = _random.NextInt(-radius, radius + 1);
                int dy = _random.NextInt(-radius, radius + 1);
                var candidate = new GridPoint(center.X + dx, center.Y + dy);

                if (grid.IsPassable(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        #endregion

        #region Monthly update

        private void OnNewMonth(NewMonthEvent evt)
        {
            long tick = evt.Tick;
            double maxShift = _settings.Get(LifestyleShiftMaxYearsPerMonthSettingId);
            double oldAgeChancePerYear = _settings.Get(OldAgeDeathChancePerYearPastLifespanSettingId);
            double oldAgeChanceCap = _settings.Get(OldAgeDeathChanceCapSettingId);

            var toKill = new List<(EntityId Id, string Cause)>();

            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows().ToList())
            {
                EntityId id = row.Key;
                PersonState person = row.Value;

                if (person.HoursTrackedThisMonth > 0)
                {
                    ApplyLifestyleShift(person, maxShift);
                }

                if (person.AgeLifeYears > person.LifespanYears)
                {
                    double yearsPast = person.AgeLifeYears - person.LifespanYears;
                    double chance = Math.Min(oldAgeChanceCap, yearsPast * oldAgeChancePerYear);
                    if (_random.Chance((float)chance))
                    {
                        toKill.Add((id, "old_age"));
                    }
                }

                person.HoursTrackedThisMonth = 0;
                person.HoursHungerCriticalThisMonth = 0;
                person.HomelessNightsThisMonth = 0;
                person.HygieneSumThisMonth = 0.0;
                person.HygieneSamplesThisMonth = 0;
                person.HoursBadMoodThisMonth = 0;
                person.HardLaborHoursThisMonth = 0;
            }

            foreach ((EntityId id, string cause) in toKill)
            {
                KillPerson(id, cause, tick);
            }
        }

        /// <summary>
        /// Combines this month's food, shelter, hygiene, hard labor and
        /// stress into a -1..+1 score, scaled by the max-years-per-month
        /// setting. One factor per named input the design calls out, so a
        /// later prompt can fill in doctors/environment (not yet given an
        /// input at all) without touching the others.
        /// </summary>
        private void ApplyLifestyleShift(PersonState person, double maxShiftYears)
        {
            double foodScore = 1.0 - 2.0 * ((double)person.HoursHungerCriticalThisMonth / person.HoursTrackedThisMonth);
            double shelterScore = 1.0 - 2.0 * ((double)person.HomelessNightsThisMonth / CalendarConstants.DaysPerMonth);
            double hygieneScore = person.HygieneSamplesThisMonth > 0
                ? 2.0 * (person.HygieneSumThisMonth / person.HygieneSamplesThisMonth) / 100.0 - 1.0
                : 0.0;
            double hardLaborScore = 1.0 - 2.0 * ((double)person.HardLaborHoursThisMonth / person.HoursTrackedThisMonth);
            double stressScore = 1.0 - 2.0 * ((double)person.HoursBadMoodThisMonth / person.HoursTrackedThisMonth);

            double overall = (foodScore + shelterScore + hygieneScore + hardLaborScore + stressScore) / 5.0;
            overall = Math.Clamp(overall, -1.0, 1.0);

            person.LifespanYears = Math.Max(1.0, person.LifespanYears + overall * maxShiftYears);
        }

        private void KillPerson(EntityId id, string cause, long tick)
        {
            _events.Post(PersonDiedEventId, tick, new EventData().SetEntity("person", id).SetString("cause", cause));
            _entities.Destroy(id);
        }

        #endregion

        #region Movement integration

        private void OnMoverArrived(PostedEvent posted)
        {
            EntityId id = posted.Data.GetEntity("mover");
            if (_people.TryGet(id, out PersonState person) && (person.Activity == PersonActivity.Wandering || person.Activity == PersonActivity.GoingHome || person.Activity == PersonActivity.Commuting))
            {
                person.Activity = PersonActivity.Idle;
            }
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_people.Count);
            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows())
            {
                writer.WriteEntityId(row.Key);
                PersonState person = row.Value;

                writer.WriteString(person.FirstName);
                writer.WriteString(person.FamilyName);
                writer.WriteDouble(person.AgeLifeYears);
                writer.WriteDouble(person.LifespanYears);
                writer.WriteDouble(person.Health);
                writer.WriteBool(person.IsBeliever);
                writer.WriteDouble(person.Mood);
                writer.WriteInt((int)person.Activity);
                writer.WriteDouble(person.HoursHungerEmpty);

                writer.WriteInt(person.HoursTrackedThisMonth);
                writer.WriteInt(person.HoursHungerCriticalThisMonth);
                writer.WriteInt(person.HomelessNightsThisMonth);
                writer.WriteDouble(person.HygieneSumThisMonth);
                writer.WriteInt(person.HygieneSamplesThisMonth);
                writer.WriteInt(person.HoursBadMoodThisMonth);
                writer.WriteInt(person.HardLaborHoursThisMonth);

                writer.WriteInt(person.Skills.Length);
                for (int i = 0; i < person.Skills.Length; i++)
                {
                    writer.WriteInt(person.Skills[i]);
                    writer.WriteDouble(person.SkillProgress[i]);
                }

                List<string> needIds = person.Needs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                writer.WriteInt(needIds.Count);
                foreach (string needId in needIds)
                {
                    PersonNeedState need = person.Needs[needId];
                    writer.WriteString(needId);
                    writer.WriteDouble(need.Meter);
                    writer.WriteInt((int)need.Threshold);
                }
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var person = new PersonState
                {
                    FirstName = reader.ReadString(),
                    FamilyName = reader.ReadString(),
                    AgeLifeYears = reader.ReadDouble(),
                    LifespanYears = reader.ReadDouble(),
                    Health = reader.ReadDouble(),
                    IsBeliever = reader.ReadBool(),
                    Mood = reader.ReadDouble(),
                    Activity = (PersonActivity)reader.ReadInt(),
                    HoursHungerEmpty = reader.ReadDouble()
                };

                person.HoursTrackedThisMonth = reader.ReadInt();
                person.HoursHungerCriticalThisMonth = reader.ReadInt();
                person.HomelessNightsThisMonth = reader.ReadInt();
                person.HygieneSumThisMonth = reader.ReadDouble();
                person.HygieneSamplesThisMonth = reader.ReadInt();
                person.HoursBadMoodThisMonth = reader.ReadInt();
                person.HardLaborHoursThisMonth = reader.ReadInt();

                int skillCount = reader.ReadInt();
                for (int s = 0; s < skillCount; s++)
                {
                    int value = reader.ReadInt();
                    double progress = reader.ReadDouble();
                    if (s < person.Skills.Length)
                    {
                        person.Skills[s] = value;
                        person.SkillProgress[s] = progress;
                    }
                }

                int needCount = reader.ReadInt();
                for (int n = 0; n < needCount; n++)
                {
                    string needId = reader.ReadString();
                    var need = new PersonNeedState
                    {
                        Meter = (float)reader.ReadDouble(),
                        Threshold = (NeedThresholdState)reader.ReadInt()
                    };
                    person.Needs[needId] = need;
                }

                _people.Add(id, person);
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (!_people.TryGet(entity, out PersonState person))
            {
                yield break;
            }

            yield return $"{person.FullName}, age {person.AgeLifeYears:F1} (lifespan {person.LifespanYears:F1}), health {person.Health:F0}, mood {person.Mood:F0}";
            yield return $"activity: {person.Activity}, believer: {person.IsBeliever}, class: {_classProvider(entity)}";

            var skillLines = new List<string>();
            for (int i = 0; i < person.Skills.Length; i++)
            {
                if (person.Skills[i] > 0)
                {
                    skillLines.Add($"{(Skill)i} {person.Skills[i]}");
                }
            }

            if (skillLines.Count > 0)
            {
                yield return $"skills: {string.Join(", ", skillLines)}";
            }

            foreach (KeyValuePair<string, PersonNeedState> need in person.Needs.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                yield return $"  {need.Key}: {need.Value.Meter:F0} ({need.Value.Threshold})";
            }
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("people", "people - lists every person briefly.", args =>
            {
                List<string> lines = _people.Rows()
                    .Select(row => $"{row.Key}: {row.Value.FullName}, age {row.Value.AgeLifeYears:F1}, mood {row.Value.Mood:F0}, {row.Value.Activity}")
                    .ToList();

                return lines.Count == 0 ? "No people." : string.Join("\n", lines);
            }));

            context.RegisterCommand(new ConsoleCommand("person", "person <entityId> - shows one person in full.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !_people.Has(id))
                {
                    return "Usage: person <entityId> (e.g. e12.3)";
                }

                return string.Join("\n", Inspect(id));
            }));

            context.RegisterCommand(new ConsoleCommand("kill", "kill <entityId> - kills a person, for testing.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !_people.Has(id))
                {
                    return "Usage: kill <entityId> (e.g. e12.3)";
                }

                KillPerson(id, "killed", context.CurrentTick);
                return $"{id} killed.";
            }));
        }

        #endregion
    }
}
