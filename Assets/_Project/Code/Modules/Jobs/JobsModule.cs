using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Modules.Jobs
{
    /// <summary>
    /// Openings, applications, hiring, shifts and pay for every building's
    /// worker/manager/trainer slots, plus a construction site's temporary
    /// builder slot. Depends on Buildings (slot counts, construction
    /// progress), Wealth (pay transfers and the job-tier hook), People
    /// (skills, mood, the workplace/shift provider) and map/movement (grid
    /// distance for applications).
    /// </summary>
    public sealed class JobsModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.jobs";

        public const string DayShiftStartHourSettingId = "setting.jobs.day_shift_start_hour";
        public const string DayShiftEndHourSettingId = "setting.jobs.day_shift_end_hour";
        public const string NightShiftStartHourSettingId = "setting.jobs.night_shift_start_hour";
        public const string NightShiftEndHourSettingId = "setting.jobs.night_shift_end_hour";
        public const string SwitchRequiresPercentMoreSettingId = "setting.jobs.switch_requires_percent_more";
        public const string UnpaidDaysBeforeQuitSettingId = "setting.jobs.unpaid_days_before_quit";
        public const string BadMoodDaysBeforeQuitSettingId = "setting.jobs.bad_mood_days_before_quit";
        public const string DistancePenaltyPerCellShilSettingId = "setting.jobs.distance_penalty_per_cell_shil";
        public const string SkillGainPerWorkHourSettingId = "setting.jobs.skill_gain_per_work_hour";
        public const string FireSkillShortfallSettingId = "setting.jobs.fire_skill_shortfall";
        public const string BuilderSlotsPerSiteSettingId = "setting.jobs.builder_slots_per_site";

        public const string HiredEventId = "event.jobs.hired";
        public const string QuitEventId = "event.jobs.quit";
        public const string FiredEventId = "event.jobs.fired";
        public const string UnpaidEventId = "event.jobs.unpaid";

        private sealed class HourlySystem : ISimSystem
        {
            private readonly JobsModule _owner;
            public HourlySystem(JobsModule owner) => _owner = owner;
            public string Id => ModuleId + ".hourly";
            public void Update(long tick) => _owner.UpdateHourly(tick);
        }

        private readonly BuildingsModule _buildings;
        private readonly WealthModule _wealth;
        private readonly PeopleModule _people;
        private readonly MapMovementModule _mapMovement;

        private SettingsRegistry _settings;
        private EventBus _events;

        private EntityTable<JobsState> _jobs;
        private Dictionary<string, Recipe> _recipeByWorkplaceId;

        /// <summary>Reverse index: a working person's building and slot type, for O(1) lookups from People's hourly schedule.</summary>
        private readonly Dictionary<EntityId, (EntityId Building, JobSlotType Type)> _employmentByPerson = new Dictionary<EntityId, (EntityId, JobSlotType)>();

        public JobsModule(BuildingsModule buildings, WealthModule wealth, PeopleModule people, MapMovementModule mapMovement)
        {
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            _wealth = wealth ?? throw new ArgumentNullException(nameof(wealth));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _mapMovement = mapMovement ?? throw new ArgumentNullException(nameof(mapMovement));
        }

        public string Id => ModuleId;
        public int Version => 1;

        public IReadOnlyList<string> Dependencies { get; } =
            new[] { BuildingsModule.ModuleId, WealthModule.ModuleId, PeopleModule.ModuleId, MapMovementModule.ModuleId };

        public int SectionVersion => 1;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _jobs = new EntityTable<JobsState>(context.Entities);

            LoadRecipes(context.Content);

            _wealth.SetJobTierProvider(GetJobTier);
            _people.SetWorkplaceProvider(GetWorkplaceLocation, IsOnShift);

            context.Subscribe<NewDayEvent>(OnNewDay);
            context.Subscribe(PeopleModule.PersonDiedEventId, OnPersonDied);
            context.RegisterSystem(new HourlySystem(this), CalendarConstants.TicksPerHour, 0, 0);

            RegisterCommands(context);

            if (!context.IsLoading)
            {
                SyncSlotsWithBuildings();
            }
        }

        public void Shutdown()
        {
        }

        private void LoadRecipes(ContentDatabase content)
        {
            _recipeByWorkplaceId = new Dictionary<string, Recipe>();
            foreach (ContentEntry entry in content.ListByType(BuiltInContentTypes.RecipeTypeName))
            {
                var recipe = (Recipe)entry.Definition;
                if (!recipe.Workplace.IsEmpty)
                {
                    _recipeByWorkplaceId[recipe.Workplace.Id] = recipe;
                }
            }
        }

        #region Slot groups

        public IEnumerable<KeyValuePair<EntityId, JobsState>> Jobs => _jobs.Rows();

        public bool TryGetJobs(EntityId buildingId, out JobsState jobs) => _jobs.TryGet(buildingId, out jobs);

        /// <summary>Creates a builder opening for every under-construction site with none yet, real worker/manager/trainer
        /// slots for every newly finished building, and clears a finished site's now-irrelevant builder slot.</summary>
        private void SyncSlotsWithBuildings()
        {
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Buildings)
            {
                EntityId buildingId = row.Key;
                BuildingState building = row.Value;

                if (!_jobs.TryGet(buildingId, out JobsState jobs))
                {
                    jobs = new JobsState();
                }

                if (building.Construction != null)
                {
                    if (jobs.Builder == null)
                    {
                        int count = (int)_settings.Get(BuilderSlotsPerSiteSettingId);
                        Money pay = Money.FromShil(_settings.Get(WealthModule.SkilledWagePerDaySettingId));
                        jobs.Builder = NewGroup(WorkerTier.Skilled, Skill.Construction, pay, count);
                    }
                }
                else
                {
                    if (jobs.Builder != null)
                    {
                        VacateAll(jobs.Builder);
                        jobs.Builder = null;
                    }

                    EnsureOperatingSlots(jobs, row.Value.BuildingTypeId);
                }

                if (jobs.Builder != null || jobs.Worker != null || jobs.Manager != null || jobs.Trainer != null)
                {
                    if (!_jobs.Has(buildingId))
                    {
                        _jobs.Add(buildingId, jobs);
                    }
                }
            }
        }

        private void EnsureOperatingSlots(JobsState jobs, string typeId)
        {
            BuildingType type = _buildings.GetBuildingType(typeId);
            if (type == null)
            {
                return;
            }

            if (jobs.Worker == null && type.WorkerSlots > 0)
            {
                (WorkerTier tier, Skill skill) = ResolveWorkerTierAndSkill(typeId);
                Money pay = Money.FromShil(_settings.Get(tier == WorkerTier.Skilled ? WealthModule.SkilledWagePerDaySettingId : WealthModule.LaborerWagePerDaySettingId));
                jobs.Worker = NewGroup(tier, skill, pay, type.WorkerSlots);
            }

            if (jobs.Manager == null && type.ManagerSlots > 0)
            {
                Money pay = Money.FromShil(_settings.Get(WealthModule.SkilledWagePerDaySettingId));
                jobs.Manager = NewGroup(WorkerTier.Skilled, Skill.Administration, pay, type.ManagerSlots);
            }

            if (jobs.Trainer == null && type.TrainerSlots > 0)
            {
                Money pay = Money.FromShil(_settings.Get(WealthModule.SkilledWagePerDaySettingId));
                jobs.Trainer = NewGroup(WorkerTier.Skilled, Skill.Administration, pay, type.TrainerSlots);
            }
        }

        private (WorkerTier Tier, Skill Skill) ResolveWorkerTierAndSkill(string buildingTypeId)
        {
            if (_recipeByWorkplaceId.TryGetValue(buildingTypeId, out Recipe recipe))
            {
                Skill skill = Enum.TryParse(recipe.Skill, out Skill parsed) ? parsed : Skill.Hauling;
                return (recipe.WorkerTier, skill);
            }

            return (WorkerTier.Laborer, Skill.Hauling); // no recipe yet for this type (e.g. a storehouse): generic labor
        }

        private static JobSlotGroup NewGroup(WorkerTier tier, Skill skill, Money pay, int count)
        {
            var group = new JobSlotGroup { Tier = tier, Skill = skill, PayPerDay = pay, Coverage = JobCoverage.Day };
            for (int i = 0; i < count; i++)
            {
                group.Occupants.Add(null);
                group.UnpaidDaysInARow.Add(0);
                group.BadMoodDaysInARow.Add(0);
            }

            return group;
        }

        private static JobSlotGroup GroupFor(JobsState jobs, JobSlotType type) => type switch
        {
            JobSlotType.Builder => jobs.Builder,
            JobSlotType.Worker => jobs.Worker,
            JobSlotType.Manager => jobs.Manager,
            _ => jobs.Trainer
        };

        private bool IsHardLaborBuilding(string buildingTypeId) =>
            _recipeByWorkplaceId.TryGetValue(buildingTypeId, out Recipe recipe) && recipe.IsHardLabor;

        /// <summary>A dead person can't keep a job: vacate their slot so nothing tries to pay a destroyed entity.</summary>
        private void OnPersonDied(PostedEvent posted)
        {
            EntityId person = posted.Data.GetEntity("person");
            if (!_employmentByPerson.TryGetValue(person, out (EntityId Building, JobSlotType Type) employment) || !_jobs.TryGet(employment.Building, out JobsState jobs))
            {
                return;
            }

            JobSlotGroup group = GroupFor(jobs, employment.Type);
            int index = group?.Occupants.IndexOf(person) ?? -1;
            if (group != null && index >= 0)
            {
                Vacate(group, index, person);
            }
        }

        #endregion

        #region Shifts

        private bool IsHourInCoverage(int hour, JobCoverage coverage)
        {
            int dayStart = (int)_settings.Get(DayShiftStartHourSettingId);
            int dayEnd = (int)_settings.Get(DayShiftEndHourSettingId);
            int nightStart = (int)_settings.Get(NightShiftStartHourSettingId);
            int nightEnd = (int)_settings.Get(NightShiftEndHourSettingId);

            bool inDay = IsHourInWindow(hour, dayStart, dayEnd);
            bool inNight = IsHourInWindow(hour, nightStart, nightEnd);

            return coverage switch
            {
                JobCoverage.Day => inDay,
                JobCoverage.Night => inNight,
                _ => inDay || inNight
            };
        }

        private static bool IsHourInWindow(int hour, int start, int end) =>
            start > end ? hour >= start || hour < end : hour >= start && hour < end;

        /// <summary>Whether this person currently holds any job (builder, worker, manager or trainer) at any building.</summary>
        public bool IsEmployed(EntityId person) => _employmentByPerson.ContainsKey(person);

        /// <summary>Null when the person holds no job - Gathering layers its own hand-gathering workplace underneath this one.</summary>
        public GridPoint? GetWorkplaceLocation(EntityId person) =>
            _employmentByPerson.TryGetValue(person, out (EntityId Building, JobSlotType Type) employment) ? _buildings.GetDoorstep(employment.Building) : null;

        /// <summary>This person's job slot and workplace, if they hold one - for a plain "role/job" description (the People panel).</summary>
        public bool TryGetEmployment(EntityId person, out EntityId building, out JobSlotType slotType)
        {
            if (_employmentByPerson.TryGetValue(person, out (EntityId Building, JobSlotType Type) employment))
            {
                building = employment.Building;
                slotType = employment.Type;
                return true;
            }

            building = EntityId.None;
            slotType = default;
            return false;
        }

        /// <summary>False when the person holds no job - Gathering layers its own hand-gathering shift underneath this one.</summary>
        public bool IsOnShift(EntityId person, int hour)
        {
            if (!_employmentByPerson.TryGetValue(person, out (EntityId Building, JobSlotType Type) employment) || !_jobs.TryGet(employment.Building, out JobsState jobs))
            {
                return false;
            }

            JobSlotGroup group = GroupFor(jobs, employment.Type);
            return group != null && IsHourInCoverage(hour, group.Coverage);
        }

        #endregion

        #region Hourly: work, skill gain, hard labor, pay

        private void UpdateHourly(long tick)
        {
            GameDate date = GameDate.FromTick(tick);
            int hour = date.Hour;
            double skillGainPerHour = _settings.Get(SkillGainPerWorkHourSettingId);
            int dayEndHour = (int)_settings.Get(DayShiftEndHourSettingId);
            int nightEndHour = (int)_settings.Get(NightShiftEndHourSettingId);

            foreach (KeyValuePair<EntityId, JobsState> row in _jobs.Rows().ToList())
            {
                EntityId buildingId = row.Key;
                JobsState jobs = row.Value;
                _buildings.TryGetBuilding(buildingId, out BuildingState building);

                ProcessGroupHour(buildingId, building, jobs.Builder, JobSlotType.Builder, hour, dayEndHour, nightEndHour, skillGainPerHour, tick);
                ProcessGroupHour(buildingId, building, jobs.Worker, JobSlotType.Worker, hour, dayEndHour, nightEndHour, skillGainPerHour, tick);
                ProcessGroupHour(buildingId, building, jobs.Manager, JobSlotType.Manager, hour, dayEndHour, nightEndHour, skillGainPerHour, tick);
                ProcessGroupHour(buildingId, building, jobs.Trainer, JobSlotType.Trainer, hour, dayEndHour, nightEndHour, skillGainPerHour, tick);
            }
        }

        private void ProcessGroupHour(EntityId buildingId, BuildingState building, JobSlotGroup group, JobSlotType type, int hour, int dayEndHour, int nightEndHour, double skillGainPerHour, long tick)
        {
            if (group == null)
            {
                return;
            }

            for (int i = 0; i < group.Occupants.Count; i++)
            {
                EntityId? occupant = group.Occupants[i];
                if (!occupant.HasValue)
                {
                    continue;
                }

                if (IsHourInCoverage(hour, group.Coverage) && _people.TryGetPerson(occupant.Value, out PersonState person))
                {
                    double speed = WorkSpeed.ForSkillLevel(person.Skills[(int)group.Skill]);
                    _people.AddSkillExperience(occupant.Value, group.Skill, skillGainPerHour);
                    group.WorkHoursAccumulated += speed;

                    if (type == JobSlotType.Builder && building != null && building.Construction != null)
                    {
                        _buildings.TryAddLaborHours(buildingId, speed, out _);
                    }

                    if (type == JobSlotType.Worker && building != null && IsHardLaborBuilding(building.BuildingTypeId))
                    {
                        _people.RecordHardLaborHour(occupant.Value);
                    }
                }

                bool payNow = (group.Coverage != JobCoverage.Night && hour == dayEndHour) ||
                              (group.Coverage != JobCoverage.Day && hour == nightEndHour);

                if (payNow)
                {
                    PayOccupant(buildingId, building, group, i, tick);
                }
            }
        }

        private void PayOccupant(EntityId buildingId, BuildingState building, JobSlotGroup group, int slotIndex, long tick)
        {
            EntityId worker = group.Occupants[slotIndex].Value;
            AccountId employer = building != null && building.Owner.HasValue ? AccountId.Person(building.Owner.Value) : AccountId.Crown;

            if (_wealth.TryTransfer(employer, AccountId.Person(worker), group.PayPerDay, TransferReason.Wage))
            {
                group.UnpaidDaysInARow[slotIndex] = 0;
                return;
            }

            group.UnpaidDaysInARow[slotIndex]++;
            _events.Post(UnpaidEventId, tick, new EventData().SetEntity("person", worker).SetEntity("building", buildingId));

            int unpaidDaysBeforeQuit = (int)_settings.Get(UnpaidDaysBeforeQuitSettingId);
            bool neverQuits = _people.TryGetPerson(worker, out PersonState workerState) && workerState.NeverRefusesOrFails;
            if (!neverQuits && group.UnpaidDaysInARow[slotIndex] >= unpaidDaysBeforeQuit)
            {
                Vacate(group, slotIndex, worker);
                _events.Post(QuitEventId, tick, new EventData().SetEntity("person", worker).SetEntity("building", buildingId).SetString("reason", "unpaid"));
            }
        }

        #endregion

        #region Daily: bad mood quitting, low-skill firing, applications and hiring

        private void OnNewDay(NewDayEvent evt)
        {
            long tick = evt.Tick;
            SyncSlotsWithBuildings();
            ProcessBadMoodAndLowSkill(tick);
            RunApplicationsAndHiring(tick);
        }

        private void ProcessBadMoodAndLowSkill(long tick)
        {
            double badMoodThreshold = _settings.Get(PeopleModule.BadMoodThresholdSettingId);
            int badMoodDaysBeforeQuit = (int)_settings.Get(BadMoodDaysBeforeQuitSettingId);
            int fireShortfall = (int)_settings.Get(FireSkillShortfallSettingId);

            foreach (KeyValuePair<EntityId, JobsState> row in _jobs.Rows().ToList())
            {
                EntityId buildingId = row.Key;
                foreach (JobSlotGroup group in AllGroups(row.Value))
                {
                    if (group == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < group.Occupants.Count; i++)
                    {
                        EntityId? occupant = group.Occupants[i];
                        if (!occupant.HasValue || !_people.TryGetPerson(occupant.Value, out PersonState person))
                        {
                            continue;
                        }

                        if (group.MinimumSkill > 0 && person.Skills[(int)group.Skill] < group.MinimumSkill - fireShortfall)
                        {
                            EntityId fired = occupant.Value;
                            Vacate(group, i, fired);
                            _events.Post(FiredEventId, tick, new EventData().SetEntity("person", fired).SetEntity("building", buildingId).SetString("reason", "low_skill"));
                            continue;
                        }

                        if (person.NeverRefusesOrFails)
                        {
                            continue; // founders never leave a job for bad mood
                        }

                        if (person.Mood < badMoodThreshold)
                        {
                            group.BadMoodDaysInARow[i]++;
                        }
                        else
                        {
                            group.BadMoodDaysInARow[i] = 0;
                        }

                        if (group.BadMoodDaysInARow[i] >= badMoodDaysBeforeQuit)
                        {
                            EntityId quitter = occupant.Value;
                            Vacate(group, i, quitter);
                            _events.Post(QuitEventId, tick, new EventData().SetEntity("person", quitter).SetEntity("building", buildingId).SetString("reason", "unhappy"));
                        }
                    }
                }
            }
        }

        private static IEnumerable<JobSlotGroup> AllGroups(JobsState jobs)
        {
            yield return jobs.Builder;
            yield return jobs.Worker;
            yield return jobs.Manager;
            yield return jobs.Trainer;
        }

        private readonly struct OpeningKey
        {
            public readonly EntityId Building;
            public readonly JobSlotType Type;
            public readonly int Index;

            public OpeningKey(EntityId building, JobSlotType type, int index)
            {
                Building = building;
                Type = type;
                Index = index;
            }
        }

        private void RunApplicationsAndHiring(long tick)
        {
            var openings = new List<OpeningKey>();
            foreach (KeyValuePair<EntityId, JobsState> row in _jobs.Rows())
            {
                foreach (JobSlotType type in new[] { JobSlotType.Builder, JobSlotType.Worker, JobSlotType.Manager, JobSlotType.Trainer })
                {
                    JobSlotGroup group = GroupFor(row.Value, type);
                    if (group == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < group.Occupants.Count; i++)
                    {
                        if (!group.Occupants[i].HasValue)
                        {
                            openings.Add(new OpeningKey(row.Key, type, i));
                        }
                    }
                }
            }

            if (openings.Count == 0)
            {
                return;
            }

            double percentMore = _settings.Get(SwitchRequiresPercentMoreSettingId);
            double distancePenaltyPerCell = _settings.Get(DistancePenaltyPerCellShilSettingId);
            int adultAge = (int)_settings.Get(PeopleModule.AdultAgeYearsSettingId);

            var applicantsByOpening = new Dictionary<OpeningKey, List<EntityId>>();

            foreach (KeyValuePair<EntityId, PersonState> personRow in _people.People.ToList())
            {
                EntityId personId = personRow.Key;
                PersonState person = personRow.Value;
                if (person.AgeLifeYears < adultAge || person.IsMonarch)
                {
                    continue;
                }

                bool isEmployed = _employmentByPerson.TryGetValue(personId, out (EntityId Building, JobSlotType Type) current);
                Money currentPay = Money.Zero;
                if (isEmployed && _jobs.TryGet(current.Building, out JobsState currentJobs))
                {
                    JobSlotGroup currentGroup = GroupFor(currentJobs, current.Type);
                    if (currentGroup != null)
                    {
                        currentPay = currentGroup.PayPerDay;
                    }
                }

                GridPoint origin = _people.GetHomeLocation(personId) ?? _mapMovement.GetCell(personId);
                SocialClass personClass = _wealth.GetClass(personId);

                OpeningKey? best = null;
                Money bestPay = Money.Zero;
                double bestScore = double.NegativeInfinity;

                foreach (OpeningKey opening in openings)
                {
                    if (!_jobs.TryGet(opening.Building, out JobsState jobs))
                    {
                        continue;
                    }

                    JobSlotGroup group = GroupFor(jobs, opening.Type);
                    if (group == null || (group.AllowedClassesMask & (1 << (int)personClass)) == 0)
                    {
                        continue;
                    }

                    if (person.Skills[(int)group.Skill] < group.MinimumSkill)
                    {
                        continue;
                    }

                    GridPoint? workplace = _buildings.GetDoorstep(opening.Building);
                    int distance = workplace.HasValue ? ManhattanDistance(origin, workplace.Value) : 0;
                    double score = group.PayPerDay.HundredthsOfShil - distancePenaltyPerCell * 100.0 * distance;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = opening;
                        bestPay = group.PayPerDay;
                    }
                }

                if (best == null)
                {
                    continue;
                }

                if (isEmployed && bestPay.HundredthsOfShil < currentPay.HundredthsOfShil * (1.0 + percentMore / 100.0))
                {
                    continue; // not enough of a raise to give up a job they already have
                }

                if (!applicantsByOpening.TryGetValue(best.Value, out List<EntityId> applicants))
                {
                    applicants = new List<EntityId>();
                    applicantsByOpening[best.Value] = applicants;
                }

                applicants.Add(personId);
            }

            foreach (KeyValuePair<OpeningKey, List<EntityId>> entry in applicantsByOpening
                         .OrderBy(e => e.Key.Building.Index)
                         .ThenBy(e => e.Key.Type)
                         .ThenBy(e => e.Key.Index))
            {
                Hire(entry.Key, entry.Value, tick);
            }
        }

        private static int ManhattanDistance(GridPoint a, GridPoint b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private void Hire(OpeningKey opening, List<EntityId> applicants, long tick)
        {
            if (!_jobs.TryGet(opening.Building, out JobsState jobs))
            {
                return;
            }

            JobSlotGroup group = GroupFor(jobs, opening.Type);
            if (group == null || group.Occupants[opening.Index].HasValue)
            {
                return; // already filled by an earlier opening this same pass, or no longer exists
            }

            EntityId best = applicants
                .OrderByDescending(p => _people.TryGetPerson(p, out PersonState state) ? state.Skills[(int)group.Skill] : 0)
                .ThenBy(p => p.Index)
                .First();

            if (_employmentByPerson.TryGetValue(best, out (EntityId Building, JobSlotType Type) previous))
            {
                if (_jobs.TryGet(previous.Building, out JobsState previousJobs))
                {
                    JobSlotGroup previousGroup = GroupFor(previousJobs, previous.Type);
                    if (previousGroup != null)
                    {
                        int previousIndex = previousGroup.Occupants.IndexOf(best);
                        if (previousIndex >= 0)
                        {
                            Vacate(previousGroup, previousIndex, best);
                        }
                    }
                }
            }

            group.Occupants[opening.Index] = best;
            group.UnpaidDaysInARow[opening.Index] = 0;
            group.BadMoodDaysInARow[opening.Index] = 0;
            _employmentByPerson[best] = (opening.Building, opening.Type);

            _events.Post(HiredEventId, tick, new EventData().SetEntity("person", best).SetEntity("building", opening.Building));
        }

        private void Vacate(JobSlotGroup group, int slotIndex, EntityId person)
        {
            group.Occupants[slotIndex] = null;
            group.UnpaidDaysInARow[slotIndex] = 0;
            group.BadMoodDaysInARow[slotIndex] = 0;
            _employmentByPerson.Remove(person);
        }

        private void VacateAll(JobSlotGroup group)
        {
            for (int i = 0; i < group.Occupants.Count; i++)
            {
                if (group.Occupants[i].HasValue)
                {
                    Vacate(group, i, group.Occupants[i].Value);
                }
            }
        }

        #endregion

        #region Government slot editing

        /// <summary>The player/ministers editing a government building's slot (pay, coverage, minimum on duty, minimum skill, allowed classes). Applied next tick: it only changes stored config, read fresh by the hourly/daily passes. Refused for a private building - the owner's slots are set automatically (3.11 will let owners adjust them).</summary>
        public bool TrySetGovernmentSlotConfig(EntityId buildingId, JobSlotType type, Money? payPerDay, JobCoverage? coverage, int? minimumOnDuty, int? minimumSkill, int? allowedClassesMask, out string error)
        {
            if (!_buildings.TryGetBuilding(buildingId, out BuildingState building) || building.Owner.HasValue)
            {
                error = "Only a government building's slots can be edited this way.";
                return false;
            }

            if (!_jobs.TryGet(buildingId, out JobsState jobs))
            {
                error = $"{buildingId} has no job slots.";
                return false;
            }

            JobSlotGroup group = GroupFor(jobs, type);
            if (group == null)
            {
                error = $"{buildingId} has no {type} slots.";
                return false;
            }

            if (payPerDay.HasValue)
            {
                group.PayPerDay = payPerDay.Value;
            }

            if (coverage.HasValue)
            {
                group.Coverage = coverage.Value;
            }

            if (minimumOnDuty.HasValue)
            {
                group.MinimumOnDuty = minimumOnDuty.Value;
            }

            if (minimumSkill.HasValue)
            {
                group.MinimumSkill = minimumSkill.Value;
            }

            if (allowedClassesMask.HasValue)
            {
                group.AllowedClassesMask = allowedClassesMask.Value;
            }

            error = null;
            return true;
        }

        #endregion

        #region Class hook

        private SocialClass GetJobTier(EntityId person)
        {
            if (!_employmentByPerson.TryGetValue(person, out (EntityId Building, JobSlotType Type) employment) || !_jobs.TryGet(employment.Building, out JobsState jobs))
            {
                return SocialClass.Lower;
            }

            JobSlotGroup group = GroupFor(jobs, employment.Type);
            if (group == null)
            {
                return SocialClass.Lower;
            }

            bool isGovernmentJob = !_buildings.TryGetBuilding(employment.Building, out BuildingState building) || !building.Owner.HasValue;
            if (isGovernmentJob)
            {
                return SocialClass.Lower; // government jobs don't set class directly
            }

            return group.Tier == WorkerTier.Skilled ? SocialClass.Middle : SocialClass.Lower;
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("jobs", "jobs - summarizes employment across every building.", args =>
            {
                int filled = 0;
                int vacant = 0;
                foreach (KeyValuePair<EntityId, JobsState> row in _jobs.Rows())
                {
                    foreach (JobSlotGroup group in AllGroups(row.Value))
                    {
                        if (group == null)
                        {
                            continue;
                        }

                        filled += group.Occupants.Count(o => o.HasValue);
                        vacant += group.Occupants.Count(o => !o.HasValue);
                    }
                }

                return $"{filled} filled, {vacant} vacant, across {_jobs.Count} buildings with jobs.";
            }));

            context.RegisterCommand(new ConsoleCommand("openings", "openings - lists every vacant slot.", args =>
            {
                var lines = new List<string>();
                foreach (KeyValuePair<EntityId, JobsState> row in _jobs.Rows())
                {
                    foreach (JobSlotType type in new[] { JobSlotType.Builder, JobSlotType.Worker, JobSlotType.Manager, JobSlotType.Trainer })
                    {
                        JobSlotGroup group = GroupFor(row.Value, type);
                        if (group == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < group.Occupants.Count; i++)
                        {
                            if (!group.Occupants[i].HasValue)
                            {
                                lines.Add($"{row.Key} {type} #{i}: {group.Tier} {group.Skill}, {group.PayPerDay}/day, {group.Coverage}");
                            }
                        }
                    }
                }

                return lines.Count == 0 ? "No openings." : string.Join("\n", lines);
            }));

            context.RegisterCommand(new ConsoleCommand("job", "job <buildingId> - shows one building's slots in full.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !_jobs.Has(id))
                {
                    return "Usage: job <buildingId> (e.g. e12.3)";
                }

                return string.Join("\n", Inspect(id));
            }));

            context.RegisterCommand(new ConsoleCommand("fire", "fire <personId> - fires a government employee. Private staff can't be fired by the crown.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId personId))
                {
                    return "Usage: fire <personId> (e.g. e12.3)";
                }

                if (!_employmentByPerson.TryGetValue(personId, out (EntityId Building, JobSlotType Type) employment))
                {
                    return $"{personId} doesn't hold a job.";
                }

                if (!_buildings.TryGetBuilding(employment.Building, out BuildingState building) || building.Owner.HasValue)
                {
                    return "Only government staff can be fired by the crown.";
                }

                if (!_jobs.TryGet(employment.Building, out JobsState jobs))
                {
                    return $"{personId} doesn't hold a job.";
                }

                JobSlotGroup group = GroupFor(jobs, employment.Type);
                int index = group?.Occupants.IndexOf(personId) ?? -1;
                if (group == null || index < 0)
                {
                    return $"{personId} doesn't hold a job.";
                }

                Vacate(group, index, personId);
                _events.Post(FiredEventId, context.CurrentTick, new EventData().SetEntity("person", personId).SetEntity("building", employment.Building).SetString("reason", "dismissed"));
                return $"{personId} fired.";
            }));
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_jobs.Count);
            foreach (KeyValuePair<EntityId, JobsState> row in _jobs.Rows())
            {
                writer.WriteEntityId(row.Key);
                WriteGroup(writer, row.Value.Builder);
                WriteGroup(writer, row.Value.Worker);
                WriteGroup(writer, row.Value.Manager);
                WriteGroup(writer, row.Value.Trainer);
            }
        }

        private static void WriteGroup(SaveWriter writer, JobSlotGroup group)
        {
            writer.WriteBool(group != null);
            if (group == null)
            {
                return;
            }

            writer.WriteInt((int)group.Tier);
            writer.WriteInt((int)group.Skill);
            writer.WriteLong(group.PayPerDay.HundredthsOfShil);
            writer.WriteInt((int)group.Coverage);
            writer.WriteInt(group.MinimumOnDuty);
            writer.WriteInt(group.MinimumSkill);
            writer.WriteInt(group.AllowedClassesMask);
            writer.WriteDouble(group.WorkHoursAccumulated);

            writer.WriteInt(group.Occupants.Count);
            for (int i = 0; i < group.Occupants.Count; i++)
            {
                writer.WriteBool(group.Occupants[i].HasValue);
                if (group.Occupants[i].HasValue)
                {
                    writer.WriteEntityId(group.Occupants[i].Value);
                }

                writer.WriteInt(group.UnpaidDaysInARow[i]);
                writer.WriteInt(group.BadMoodDaysInARow[i]);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId buildingId = reader.ReadEntityId();
                var jobs = new JobsState
                {
                    Builder = ReadGroup(reader, buildingId, JobSlotType.Builder),
                    Worker = ReadGroup(reader, buildingId, JobSlotType.Worker),
                    Manager = ReadGroup(reader, buildingId, JobSlotType.Manager),
                    Trainer = ReadGroup(reader, buildingId, JobSlotType.Trainer)
                };

                _jobs.Add(buildingId, jobs);
            }
        }

        private JobSlotGroup ReadGroup(SaveReader reader, EntityId buildingId, JobSlotType type)
        {
            if (!reader.ReadBool())
            {
                return null;
            }

            var group = new JobSlotGroup
            {
                Tier = (WorkerTier)reader.ReadInt(),
                Skill = (Skill)reader.ReadInt(),
                PayPerDay = Money.FromHundredthsOfShil(reader.ReadLong()),
                Coverage = (JobCoverage)reader.ReadInt(),
                MinimumOnDuty = reader.ReadInt(),
                MinimumSkill = reader.ReadInt(),
                AllowedClassesMask = reader.ReadInt(),
                WorkHoursAccumulated = reader.ReadDouble()
            };

            int slotCount = reader.ReadInt();
            for (int i = 0; i < slotCount; i++)
            {
                EntityId? occupant = reader.ReadBool() ? reader.ReadEntityId() : (EntityId?)null;
                group.Occupants.Add(occupant);
                group.UnpaidDaysInARow.Add(reader.ReadInt());
                group.BadMoodDaysInARow.Add(reader.ReadInt());

                if (occupant.HasValue)
                {
                    _employmentByPerson[occupant.Value] = (buildingId, type);
                }
            }

            return group;
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (_jobs.TryGet(entity, out JobsState jobs))
            {
                foreach (string line in InspectBuilding(jobs))
                {
                    yield return line;
                }

                yield break;
            }

            if (_employmentByPerson.TryGetValue(entity, out (EntityId Building, JobSlotType Type) employment) &&
                _jobs.TryGet(employment.Building, out JobsState employerJobs))
            {
                JobSlotGroup group = GroupFor(employerJobs, employment.Type);
                if (group != null)
                {
                    yield return $"job: {employment.Type} at {employment.Building} ({group.Tier} {group.Skill}), pay {group.PayPerDay}/day, {group.Coverage} shift";
                }
            }
        }

        private static IEnumerable<string> InspectBuilding(JobsState jobs)
        {
            foreach ((JobSlotType type, JobSlotGroup group) in new[]
                     {
                         (JobSlotType.Builder, jobs.Builder), (JobSlotType.Worker, jobs.Worker),
                         (JobSlotType.Manager, jobs.Manager), (JobSlotType.Trainer, jobs.Trainer)
                     })
            {
                if (group == null)
                {
                    continue;
                }

                yield return $"{type}: {group.Tier} {group.Skill}, {group.PayPerDay}/day, {group.Coverage}, minimum on duty {group.MinimumOnDuty}";
                for (int i = 0; i < group.Occupants.Count; i++)
                {
                    yield return group.Occupants[i].HasValue ? $"  #{i}: {group.Occupants[i].Value}" : $"  #{i}: vacant";
                }
            }
        }

        #endregion
    }
}
