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
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;

namespace TheEmptying.Modules.Gathering
{
    /// <summary>
    /// Hand gathering: woodcutting, quarrying and farming done with no
    /// building, at a quarter of the real building's rate. When a crown site
    /// is waiting for labor, up to a handful of founders farm wheat (only
    /// when stored food is short and it's in season) and the rest build it
    /// unpaid; otherwise founders (and anyone else jobless) farm wheat under
    /// the same cap and conditions, or gather whichever raw material is
    /// scarcer. Self-foraging ground grubs (PeopleModule's own eating logic,
    /// not a task here) is the separate, immediate fallback once a person is
    /// hungry with no bread at all. Non-founders keep and sell their own
    /// output to the crown storehouse at market price. A gatherer's task is
    /// decided each morning (and immediately when the stockpile is placed or
    /// a new site goes up) and worked each hour, underneath whatever job Jobs
    /// already gives a person. Depends on People (who's jobless, skills),
    /// Buildings (the crown storehouse and sites waiting for labor), Jobs
    /// (who already has a real job), Inventory (where output lands), Market
    /// (the crown's purchase from a non-founder) and map/movement (travel to
    /// the gather spot).
    /// </summary>
    public sealed class GatheringModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.gathering";

        public const string HandLogHoursPerUnitSettingId = "setting.gathering.hand_log_hours_per_unit";
        public const string HandStoneHoursPerUnitSettingId = "setting.gathering.hand_stone_hours_per_unit";
        public const string HandWheatPerHarvestSettingId = "setting.gathering.hand_wheat_per_harvest";
        public const string RadiusCellsSettingId = "setting.gathering.radius_cells";
        public const string FoodNeededPerPersonPerMonthSettingId = "setting.gathering.food_needed_per_person_per_month";
        public const string WheatFarmerCapSettingId = "setting.gathering.wheat_farmer_cap";

        private const string LogGoodId = "good.log";
        private const string StoneGoodId = "good.stone";
        private const string WheatGoodId = "good.wheat";
        private const string LoafGoodId = "good.loaf";

        private sealed class HourlySystem : ISimSystem
        {
            private readonly GatheringModule _owner;
            public HourlySystem(GatheringModule owner) => _owner = owner;
            public string Id => ModuleId + ".hourly";
            public void Update(long tick) => _owner.UpdateHourly(tick);
        }

        private readonly PeopleModule _people;
        private readonly BuildingsModule _buildings;
        private readonly JobsModule _jobs;
        private readonly InventoryModule _inventory;
        private readonly MarketModule _market;
        private readonly MapMovementModule _mapMovement;

        private SettingsRegistry _settings;
        private SimRandom _random;
        private Func<long> _currentTick;

        private EntityTable<GatherTaskState> _tasks;
        private Recipe _farmRecipe;

        public GatheringModule(PeopleModule people, BuildingsModule buildings, JobsModule jobs, InventoryModule inventory, MarketModule market, MapMovementModule mapMovement)
        {
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _mapMovement = mapMovement ?? throw new ArgumentNullException(nameof(mapMovement));
        }

        public string Id => ModuleId;
        public int Version => 1;

        public IReadOnlyList<string> Dependencies { get; } =
            new[] { PeopleModule.ModuleId, BuildingsModule.ModuleId, JobsModule.ModuleId, InventoryModule.ModuleId, MarketModule.ModuleId, MapMovementModule.ModuleId };

        public int SectionVersion => 1;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _random = context.Random;
            _currentTick = () => context.CurrentTick;
            _tasks = new EntityTable<GatherTaskState>(context.Entities);

            LoadFarmRecipe(context.Content);

            _people.SetWorkplaceProvider(
                personId => _jobs.GetWorkplaceLocation(personId) ?? GetLocation(personId),
                (personId, hour) => _jobs.IsOnShift(personId, hour) || IsWorkingHour(personId, hour));

            context.Subscribe<NewDayEvent>(evt => AssignTasks());
            // So builders start the same day a site goes up, not the next morning.
            context.Subscribe(BuildingsModule.ConstructionSiteStartedEventId, _ => AssignTasks());
            // So founders start their very first task the moment the stockpile goes up, not whenever
            // the first natural day boundary happens to fall.
            context.Subscribe(BuildingsModule.InitialStockpilePlacedEventId, _ => AssignTasks());
            context.RegisterSystem(new HourlySystem(this), CalendarConstants.TicksPerHour, 0, 0);

            RegisterCommands(context);
        }

        public void Shutdown()
        {
        }

        private void LoadFarmRecipe(ContentDatabase content)
        {
            foreach (ContentEntry entry in content.ListByType(BuiltInContentTypes.RecipeTypeName))
            {
                var recipe = (Recipe)entry.Definition;
                if (recipe.Outputs.Any(o => o.Good.Id == WheatGoodId))
                {
                    _farmRecipe = recipe;
                    return;
                }
            }
        }

        #region Daily task assignment

        private void AssignTasks()
        {
            int radius = (int)_settings.Get(RadiusCellsSettingId);
            int adultAge = (int)_settings.Get(PeopleModule.AdultAgeYearsSettingId);
            int wheatFarmerCap = (int)_settings.Get(WheatFarmerCapSettingId);
            GridPoint anchor = GetStockpileAnchor();

            bool anySiteWaiting = AnyCrownSiteNeedsLabor();
            bool canFarmNow = CanFarmNow();

            // Deterministic (so a save/load round trip picks the same ones) - ordered by each
            // person's entity index, which never changes once assigned. Up to the cap farm wheat;
            // while a site is waiting, only founders are eligible for this (everyone else just
            // gathers material), matching the "founders farm, the rest build" framing.
            HashSet<EntityId> farmerIds = null;
            if (canFarmNow)
            {
                IEnumerable<KeyValuePair<EntityId, PersonState>> candidates = _people.People
                    .Where(row => row.Value.AgeLifeYears >= adultAge && !_people.IsMonarch(row.Key) && !_jobs.IsEmployed(row.Key));
                if (anySiteWaiting)
                {
                    candidates = candidates.Where(row => _people.IsFounder(row.Key));
                }

                farmerIds = new HashSet<EntityId>(candidates.OrderBy(row => row.Key.Index).Take(wheatFarmerCap).Select(row => row.Key));
            }

            foreach (KeyValuePair<EntityId, PersonState> row in _people.People.ToList())
            {
                EntityId personId = row.Key;
                PersonState person = row.Value;

                bool eligible = person.AgeLifeYears >= adultAge && !_people.IsMonarch(personId) && !_jobs.IsEmployed(personId);
                if (!eligible)
                {
                    ClearTask(personId);
                    continue;
                }

                if (!_tasks.TryGet(personId, out GatherTaskState task))
                {
                    task = new GatherTaskState();
                    _tasks.Add(personId, task);
                }

                if (task.Kind == GatherTaskKind.Farm && task.Planted)
                {
                    continue; // don't abandon a crop already in the ground
                }

                bool isFarmer = farmerIds != null && farmerIds.Contains(personId);
                bool isFounder = _people.IsFounder(personId);

                if (isFounder && anySiteWaiting)
                {
                    if (isFarmer)
                    {
                        SetTask(task, GatherTaskKind.Farm, anchor, radius, null);
                    }
                    else if (TryFindCrownSiteNeedingLabor(personId, out EntityId siteId))
                    {
                        SetTask(task, GatherTaskKind.Build, anchor, radius, siteId);
                    }
                    else
                    {
                        // Shouldn't happen (a site exists somewhere, by anySiteWaiting) but fall back safely.
                        SetTask(task, PickScarcerMaterial(), anchor, radius, null);
                    }

                    continue;
                }

                SetTask(task, isFarmer ? GatherTaskKind.Farm : PickScarcerMaterial(), anchor, radius, null);
            }
        }

        private void ClearTask(EntityId personId)
        {
            if (_tasks.TryGet(personId, out GatherTaskState task))
            {
                task.Kind = GatherTaskKind.None;
                task.TargetSite = null;
            }
        }

        private void SetTask(GatherTaskState task, GatherTaskKind newKind, GridPoint anchor, int radius, EntityId? targetSite)
        {
            bool kindChanged = task.Kind != newKind;
            task.Kind = newKind;
            task.TargetSite = targetSite;

            if (newKind == GatherTaskKind.Build)
            {
                task.Spot = targetSite.HasValue ? _buildings.GetDoorstep(targetSite.Value) ?? anchor : anchor;
                return;
            }

            if (!kindChanged)
            {
                return; // keep the same spot (and, for woodcut/quarry, the accumulated hours)
            }

            task.WorkHoursAccumulated = 0;
            task.Planted = false;
            task.Spot = FindGatherSpot(anchor, radius) ?? anchor;
        }

        /// <summary>The nearest crown (unowned) construction site, if any - founders only ever build for the crown, never a private owner's site.</summary>
        private bool TryFindCrownSiteNeedingLabor(EntityId personId, out EntityId siteId)
        {
            GridPoint from = _mapMovement.GetCell(personId);
            siteId = EntityId.None;
            bool found = false;
            int bestDistance = int.MaxValue;

            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Buildings)
            {
                if (row.Value.Owner.HasValue || row.Value.Construction == null)
                {
                    continue;
                }

                GridPoint doorstep = _buildings.GetDoorstep(row.Key) ?? row.Value.Rect.Center;
                int distance = Math.Abs(from.X - doorstep.X) + Math.Abs(from.Y - doorstep.Y);
                if (!found || distance < bestDistance)
                {
                    found = true;
                    bestDistance = distance;
                    siteId = row.Key;
                }
            }

            return found;
        }

        /// <summary>Whether any crown (unowned) construction site anywhere is still waiting for labor.</summary>
        private bool AnyCrownSiteNeedsLabor()
        {
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Buildings)
            {
                if (!row.Value.Owner.HasValue && row.Value.Construction != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Below a month's placeholder supply of stored food (loaves, later other real foods) - self-foraged grubs never count, since nobody stockpiles them.</summary>
        private bool IsFoodSupplyShort()
        {
            int livingPopulation = _people.People.Count();
            double foodNeededPerMonth = livingPopulation * _settings.Get(FoodNeededPerPersonPerMonthSettingId);

            int foodInStockpile = _buildings.TryFindCrownStorehouse(out EntityId storehouseId)
                ? _inventory.GetCount(storehouseId, LoafGoodId)
                : 0;

            return foodInStockpile < foodNeededPerMonth;
        }

        /// <summary>Whether wheat farming can start right now at all: food is short and the farm recipe's season allows it.</summary>
        private bool CanFarmNow()
        {
            if (_farmRecipe == null || !IsFoodSupplyShort())
            {
                return false;
            }

            Season currentSeason = GameDate.FromTick(_currentTick()).Season;
            return _farmRecipe.SeasonsAllowed.Count == 0 || _farmRecipe.SeasonsAllowed.Contains(currentSeason);
        }

        /// <summary>Whichever raw material the crown stockpile has less of; ties favor woodcutting.</summary>
        private GatherTaskKind PickScarcerMaterial()
        {
            int logs = 0;
            int stone = 0;
            if (_buildings.TryFindCrownStorehouse(out EntityId storehouseId))
            {
                logs = _inventory.GetCount(storehouseId, LogGoodId);
                stone = _inventory.GetCount(storehouseId, StoneGoodId);
            }

            return stone < logs ? GatherTaskKind.Quarry : GatherTaskKind.Woodcut;
        }

        private GridPoint GetStockpileAnchor()
        {
            if (_buildings.TryFindCrownStorehouse(out EntityId storehouseId) && _buildings.TryGetBuilding(storehouseId, out BuildingState storehouse))
            {
                return storehouse.Rect.Center;
            }

            MapGrid grid = _mapMovement.Grid;
            return new GridPoint(grid.Width / 2, grid.Depth / 2);
        }

        /// <summary>Tries a handful of random cells within radius of anchor, falling back to null.</summary>
        private GridPoint? FindGatherSpot(GridPoint anchor, int radius)
        {
            MapGrid grid = _mapMovement.Grid;
            const int attempts = 12;

            for (int i = 0; i < attempts; i++)
            {
                int dx = _random.NextInt(-radius, radius + 1);
                int dy = _random.NextInt(-radius, radius + 1);
                var candidate = new GridPoint(anchor.X + dx, anchor.Y + dy);

                if (grid.IsPassable(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        #endregion

        /// <summary>What a person's hand-gathering task is doing right now; None if they have no active task.</summary>
        public GatherTaskKind GetTaskKind(EntityId personId) => _tasks.TryGet(personId, out GatherTaskState task) ? task.Kind : GatherTaskKind.None;

        #region Workplace provider (layered underneath Jobs)

        private GridPoint? GetLocation(EntityId personId)
        {
            if (!_tasks.TryGet(personId, out GatherTaskState task) || task.Kind == GatherTaskKind.None)
            {
                return null;
            }

            return task.Kind == GatherTaskKind.Build && task.TargetSite.HasValue
                ? _buildings.GetDoorstep(task.TargetSite.Value) ?? task.Spot
                : task.Spot;
        }

        private bool IsWorkingHour(EntityId personId, int hour)
        {
            if (_jobs.IsEmployed(personId) || !_tasks.TryGet(personId, out GatherTaskState task) || task.Kind == GatherTaskKind.None)
            {
                return false;
            }

            int dayStart = (int)_settings.Get(JobsModule.DayShiftStartHourSettingId);
            int dayEnd = (int)_settings.Get(JobsModule.DayShiftEndHourSettingId);
            return dayStart > dayEnd ? hour >= dayStart || hour < dayEnd : hour >= dayStart && hour < dayEnd;
        }

        #endregion

        #region Hourly work

        private void UpdateHourly(long tick)
        {
            GameDate date = GameDate.FromTick(tick);
            int hour = date.Hour;
            double logHoursPerUnit = _settings.Get(HandLogHoursPerUnitSettingId);
            double stoneHoursPerUnit = _settings.Get(HandStoneHoursPerUnitSettingId);
            int wheatPerHarvest = (int)_settings.Get(HandWheatPerHarvestSettingId);
            double skillGainPerHour = _settings.Get(JobsModule.SkillGainPerWorkHourSettingId);

            foreach (KeyValuePair<EntityId, GatherTaskState> row in _tasks.Rows().ToList())
            {
                EntityId personId = row.Key;
                GatherTaskState task = row.Value;

                if (task.Kind == GatherTaskKind.None || _jobs.IsEmployed(personId) || !IsWorkingHour(personId, hour))
                {
                    continue;
                }

                if (!_people.TryGetPerson(personId, out PersonState person))
                {
                    continue;
                }

                GridPoint? location = GetLocation(personId);
                if (!location.HasValue || _mapMovement.GetCell(personId) != location.Value)
                {
                    continue; // still commuting there; ChooseActivity moves them this same hour
                }

                switch (task.Kind)
                {
                    case GatherTaskKind.Build:
                        ProcessBuild(personId, person, task, skillGainPerHour);
                        break;
                    case GatherTaskKind.Woodcut:
                        ProcessHandGather(personId, person, task, Skill.Logging, logHoursPerUnit, LogGoodId, skillGainPerHour);
                        break;
                    case GatherTaskKind.Quarry:
                        ProcessHandGather(personId, person, task, Skill.Mining, stoneHoursPerUnit, StoneGoodId, skillGainPerHour);
                        break;
                    case GatherTaskKind.Farm:
                        ProcessFarm(personId, person, task, wheatPerHarvest, skillGainPerHour, tick);
                        break;
                }
            }
        }

        private void ProcessBuild(EntityId personId, PersonState person, GatherTaskState task, double skillGainPerHour)
        {
            if (!task.TargetSite.HasValue || !_buildings.TryGetBuilding(task.TargetSite.Value, out BuildingState site) || site.Construction == null)
            {
                task.Kind = GatherTaskKind.None; // finished or gone since this morning; tomorrow's AssignTasks picks something new
                return;
            }

            double speed = WorkSpeed.ForSkillLevel(person.Skills[(int)Skill.Construction]);
            _people.AddSkillExperience(personId, Skill.Construction, skillGainPerHour);
            _buildings.TryAddLaborHours(task.TargetSite.Value, speed, out _);
        }

        private void ProcessHandGather(EntityId personId, PersonState person, GatherTaskState task, Skill skill, double hoursPerUnit, string goodId, double skillGainPerHour)
        {
            double speed = WorkSpeed.ForSkillLevel(person.Skills[(int)skill]);
            _people.AddSkillExperience(personId, skill, skillGainPerHour);
            task.WorkHoursAccumulated += speed;

            if (task.WorkHoursAccumulated < hoursPerUnit)
            {
                return;
            }

            task.WorkHoursAccumulated -= hoursPerUnit;
            DepositGathered(personId, goodId, 1);
        }

        private void ProcessFarm(EntityId personId, PersonState person, GatherTaskState task, int wheatPerHarvest, double skillGainPerHour, long tick)
        {
            if (!task.Planted)
            {
                if (_farmRecipe == null)
                {
                    return;
                }

                double speed = WorkSpeed.ForSkillLevel(person.Skills[(int)Skill.Farming]);
                _people.AddSkillExperience(personId, Skill.Farming, skillGainPerHour);
                task.WorkHoursAccumulated += speed;

                if (task.WorkHoursAccumulated < _farmRecipe.DurationHours)
                {
                    return;
                }

                task.WorkHoursAccumulated = 0;
                task.Planted = true;
                task.ReadyAtTick = tick + (long)_farmRecipe.GrowthTimeMonths * CalendarConstants.TicksPerMonth;
                return;
            }

            if (tick < task.ReadyAtTick)
            {
                return;
            }

            task.Planted = false;
            DepositGathered(personId, WheatGoodId, wheatPerHarvest);
        }

        /// <summary>A founder's output belongs to the crown (straight into the storehouse, if one exists yet); everyone else keeps it and the crown immediately offers to buy it at market price.</summary>
        private void DepositGathered(EntityId personId, string goodId, int amount)
        {
            if (_people.IsFounder(personId))
            {
                if (_buildings.TryFindCrownStorehouse(out EntityId storehouseId))
                {
                    _inventory.Add(storehouseId, goodId, amount);
                }

                return;
            }

            _inventory.Add(personId, goodId, amount);
            _market.TryCrownBuyFromPerson(personId, goodId, amount, out _, out _);
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_tasks.Count);
            foreach (KeyValuePair<EntityId, GatherTaskState> row in _tasks.Rows())
            {
                writer.WriteEntityId(row.Key);
                GatherTaskState task = row.Value;

                writer.WriteInt((int)task.Kind);
                writer.WriteBool(task.TargetSite.HasValue);
                if (task.TargetSite.HasValue)
                {
                    writer.WriteEntityId(task.TargetSite.Value);
                }

                writer.WriteInt(task.Spot.X);
                writer.WriteInt(task.Spot.Y);
                writer.WriteDouble(task.WorkHoursAccumulated);
                writer.WriteBool(task.Planted);
                writer.WriteLong(task.ReadyAtTick);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var task = new GatherTaskState { Kind = (GatherTaskKind)reader.ReadInt() };

                if (reader.ReadBool())
                {
                    task.TargetSite = reader.ReadEntityId();
                }

                task.Spot = new GridPoint(reader.ReadInt(), reader.ReadInt());
                task.WorkHoursAccumulated = reader.ReadDouble();
                task.Planted = reader.ReadBool();
                task.ReadyAtTick = reader.ReadLong();

                _tasks.Add(id, task);
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (!_tasks.TryGet(entity, out GatherTaskState task) || task.Kind == GatherTaskKind.None)
            {
                yield break;
            }

            switch (task.Kind)
            {
                case GatherTaskKind.Build:
                    yield return $"gathering: building {task.TargetSite} by hand";
                    break;
                case GatherTaskKind.Farm:
                    yield return task.Planted
                        ? $"gathering: farming at ({task.Spot.X},{task.Spot.Y}), ready at tick {task.ReadyAtTick}"
                        : $"gathering: farming at ({task.Spot.X},{task.Spot.Y}), planting ({task.WorkHoursAccumulated:F1}h so far)";
                    break;
                default:
                    yield return $"gathering: {task.Kind} at ({task.Spot.X},{task.Spot.Y}), {task.WorkHoursAccumulated:F1}h accumulated";
                    break;
            }
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("gathertask", "gathertask <personId> - shows one person's current hand-gathering task.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id))
                {
                    return "Usage: gathertask <personId> (e.g. e12.3)";
                }

                List<string> lines = Inspect(id).ToList();
                return lines.Count == 0 ? $"{id} has no active gathering task." : string.Join("\n", lines);
            }));
        }

        #endregion
    }
}
