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

        /// <summary>Posted whenever a person's hand-gathering task actually changes kind - behind setting.debug.log_task_changes, GameBootstrap logs these.</summary>
        public const string TaskChangedEventId = "event.gathering.task_changed";

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
        private EventBus _events;

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
            _events = context.Events;
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
            // 3.9f: so founders reassign to material gathering (or farming) the instant a site they
            // were building finishes, instead of sitting idle (ProcessBuild clears their task to None
            // the moment it notices the site is done) until the next morning's NewDayEvent.
            context.Subscribe(BuildingsModule.BuildingCompletedEventId, _ => AssignTasks());
            // 3.9j: a hand test found a founder stuck idle indefinitely with a real (not None) task -
            // their gather/build spot, once picked, is never revisited unless the task's kind
            // changes, so a spot that turns out to have no path from here (a disconnected passable
            // patch; grid.IsPassable alone can't tell) traps them on it forever, retrying the same
            // dead end every hour. Re-picking the spot the moment its commute fails closes that trap
            // within the hour instead of leaving it open for the rest of the game.
            context.Subscribe(MapMovementModule.MoverNoPathEventId, OnMoverNoPath);
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
                    ClearTask(personId, "no longer eligible (too young, monarch, or has a real job)");
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
                        SetTask(personId, task, GatherTaskKind.Farm, anchor, radius, null, "a crown site is waiting, but the wheat cap still has room and food isn't covered yet");
                    }
                    else if (TryFindCrownSiteNeedingLabor(personId, out EntityId siteId))
                    {
                        SetTask(personId, task, GatherTaskKind.Build, anchor, radius, siteId, "a crown site is waiting for builders");
                    }
                    else
                    {
                        // Shouldn't happen (a site exists somewhere, by anySiteWaiting) but fall back safely.
                        SetTask(personId, task, PickScarcerMaterial(), anchor, radius, null, "a crown site is waiting, but none was reachable right now");
                    }

                    continue;
                }

                SetTask(personId, task, isFarmer ? GatherTaskKind.Farm : PickScarcerMaterial(), anchor, radius, null,
                    isFarmer ? "nothing to build and the wheat cap still has room" : "nothing to build; gathering whichever raw material is scarcer");
            }
        }

        /// <summary>Whether any adult, non-monarch, non-employed person currently has no gathering task at all - see UpdateHourly.</summary>
        private bool AnyEligibleAdultIsUntasked()
        {
            int adultAge = (int)_settings.Get(PeopleModule.AdultAgeYearsSettingId);

            foreach (KeyValuePair<EntityId, PersonState> row in _people.People)
            {
                EntityId personId = row.Key;
                PersonState person = row.Value;
                bool eligible = person.AgeLifeYears >= adultAge && !_people.IsMonarch(personId) && !_jobs.IsEmployed(personId);

                if (eligible && GetTaskKind(personId) == GatherTaskKind.None)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearTask(EntityId personId, string reason)
        {
            if (_tasks.TryGet(personId, out GatherTaskState task))
            {
                PostTaskChanged(personId, task.Kind, GatherTaskKind.None, reason);
                task.Kind = GatherTaskKind.None;
                task.TargetSite = null;
            }
        }

        private void SetTask(EntityId personId, GatherTaskState task, GatherTaskKind newKind, GridPoint anchor, int radius, EntityId? targetSite, string reason)
        {
            bool kindChanged = task.Kind != newKind;
            if (kindChanged)
            {
                PostTaskChanged(personId, task.Kind, newKind, reason);
            }

            task.Kind = newKind;
            task.TargetSite = targetSite;

            if (newKind == GatherTaskKind.Build)
            {
                // Recomputed every call (not just when kindChanged), same as before - but spread
                // around the site's edge instead of everyone sharing the single doorstep.
                task.Spot = targetSite.HasValue ? FindFreeBuildSpot(personId, targetSite.Value, anchor) : anchor;
                return;
            }

            if (!kindChanged)
            {
                return; // keep the same spot (and, for woodcut/quarry, the accumulated hours)
            }

            task.WorkHoursAccumulated = 0;
            task.Planted = false;
            task.Spot = FindGatherSpot(anchor, radius, OtherGatherSpots(personId)) ?? anchor;
        }

        /// <summary>
        /// A builder's spot: the first edge cell around the site not already claimed by another
        /// builder there, so several founders building the same site spread around its edge instead
        /// of stacking on the single doorstep. Falls back to the (shared) doorstep only once every
        /// edge cell is already taken by other builders.
        /// </summary>
        private GridPoint FindFreeBuildSpot(EntityId personId, EntityId siteId, GridPoint anchor)
        {
            var claimed = new HashSet<GridPoint>();
            foreach (KeyValuePair<EntityId, GatherTaskState> row in _tasks.Rows())
            {
                if (row.Key != personId && row.Value.Kind == GatherTaskKind.Build && row.Value.TargetSite == siteId)
                {
                    claimed.Add(row.Value.Spot);
                }
            }

            foreach (GridPoint candidate in _buildings.GetEdgeCells(siteId))
            {
                if (!claimed.Contains(candidate))
                {
                    return candidate;
                }
            }

            return _buildings.GetDoorstep(siteId) ?? anchor;
        }

        /// <summary>
        /// 3.9j: if the failed commute was to this person's current gather/build spot, pick a fresh
        /// one - a passable cell isn't necessarily a reachable one (a disconnected patch, say), and
        /// without this the same dead end would be retried every hour forever.
        /// </summary>
        private void OnMoverNoPath(PostedEvent posted)
        {
            EntityId personId = posted.Data.GetEntity("mover");
            if (!_tasks.TryGet(personId, out GatherTaskState task) || task.Kind == GatherTaskKind.None)
            {
                return;
            }

            var failedGoal = new GridPoint((int)posted.Data.GetLong("goalX"), (int)posted.Data.GetLong("goalY"));
            if (task.Spot != failedGoal)
            {
                return; // this failed move wasn't this person's gather/build commute
            }

            GridPoint anchor = GetStockpileAnchor();

            if (task.Kind == GatherTaskKind.Build && task.TargetSite.HasValue)
            {
                task.Spot = FindFreeBuildSpot(personId, task.TargetSite.Value, anchor);
            }
            else
            {
                int radius = (int)_settings.Get(RadiusCellsSettingId);
                task.Spot = FindGatherSpot(anchor, radius, OtherGatherSpots(personId)) ?? anchor;
            }
        }

        /// <summary>Every spot currently claimed by another active, non-Build hand-gathering task - so gatherers spread out from each other the same way builders do.</summary>
        private HashSet<GridPoint> OtherGatherSpots(EntityId excludePersonId)
        {
            var spots = new HashSet<GridPoint>();
            foreach (KeyValuePair<EntityId, GatherTaskState> row in _tasks.Rows())
            {
                if (row.Key != excludePersonId && row.Value.Kind != GatherTaskKind.Build && row.Value.Kind != GatherTaskKind.None)
                {
                    spots.Add(row.Value.Spot);
                }
            }

            return spots;
        }

        /// <summary>Posts TaskChangedEventId so the presentation layer can log it behind setting.debug.log_task_changes - see GameBootstrap.</summary>
        private void PostTaskChanged(EntityId personId, GatherTaskKind oldKind, GatherTaskKind newKind, string reason)
        {
            _events.Post(TaskChangedEventId, _currentTick(), new EventData()
                .SetEntity("person", personId)
                .SetString("oldTask", oldKind.ToString())
                .SetString("newTask", newKind.ToString())
                .SetString("reason", reason));
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

        /// <summary>Tries a handful of random cells within radius of anchor, avoiding any cell in excluding (another gatherer's spot), falling back to null.</summary>
        private GridPoint? FindGatherSpot(GridPoint anchor, int radius, HashSet<GridPoint> excluding = null)
        {
            MapGrid grid = _mapMovement.Grid;
            const int attempts = 12;

            for (int i = 0; i < attempts; i++)
            {
                int dx = _random.NextInt(-radius, radius + 1);
                int dy = _random.NextInt(-radius, radius + 1);
                var candidate = new GridPoint(anchor.X + dx, anchor.Y + dy);

                if (grid.IsPassable(candidate) && (excluding == null || !excluding.Contains(candidate)))
                {
                    return candidate;
                }
            }

            return null;
        }

        #endregion

        /// <summary>What a person's hand-gathering task is doing right now; None if they have no active task.</summary>
        public GatherTaskKind GetTaskKind(EntityId personId) => _tasks.TryGet(personId, out GatherTaskState task) ? task.Kind : GatherTaskKind.None;

        /// <summary>A plain, player-facing label for a task kind (e.g. for a person's world label or the People panel) - shared so both stay in sync.</summary>
        public static string DescribeTaskKind(GatherTaskKind kind) =>
            kind switch
            {
                GatherTaskKind.None => "none",
                GatherTaskKind.Build => "Building",
                GatherTaskKind.Farm => "Farming",
                GatherTaskKind.Woodcut => "Woodcutting",
                GatherTaskKind.Quarry => "Quarrying",
                _ => kind.ToString()
            };

        /// <summary>
        /// A short, plain-English reason this person currently has no gathering task - for the People
        /// panel, so nobody is ever idle during work hours with no stated reason. Only meaningful when
        /// GetTaskKind(personId) == None; returns "" otherwise.
        /// </summary>
        public string DescribeIdleReason(EntityId personId)
        {
            if (GetTaskKind(personId) != GatherTaskKind.None)
            {
                return "";
            }

            if (_jobs.IsEmployed(personId))
            {
                return "Has a real job instead.";
            }

            if (_people.IsMonarch(personId))
            {
                return "Monarch: no work expected.";
            }

            if (!_people.TryGetPerson(personId, out PersonState person))
            {
                return "Unknown person.";
            }

            int adultAge = (int)_settings.Get(PeopleModule.AdultAgeYearsSettingId);
            if (person.AgeLifeYears < adultAge)
            {
                return "Too young to work.";
            }

            // Eligible but still untasked: AssignTasks always gives an eligible adult something
            // (build, farm, or gather material) the moment it runs, so this means it simply hasn't
            // run yet since they became eligible - the next NewDayEvent, or an immediate reassignment
            // trigger (a site started, finished, or the stockpile was just placed), will cover them.
            return "No task yet: waiting for the next reassignment.";
        }

        #region Workplace provider (layered underneath Jobs)

        /// <summary>
        /// This person's current gathering task location (their own edge spot on a build site, or
        /// their gather/farm spot); null if they have no active task. Trusts task.Spot directly
        /// (SetTask already keeps it current) rather than re-deriving a build site's doorstep here,
        /// which would give every builder on the same site the identical cell again.
        /// </summary>
        public GridPoint? GetLocation(EntityId personId)
        {
            if (!_tasks.TryGet(personId, out GatherTaskState task) || task.Kind == GatherTaskKind.None)
            {
                return null;
            }

            return task.Spot;
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
            // 3.9j: AssignTasks only reruns on specific triggers (a new day, a site starting or
            // finishing, the stockpile going up) - but a founder can also become eligible again
            // between those triggers, e.g. fired from a real job mid-day (JobsModule's unpaid-day
            // payroll check and the player's "fire" command both act independently of those
            // triggers). Without this, such a founder's hand-gathering task stays at None - and
            // nothing else ever looks at them again - until the next trigger happens to fire, which
            // could be most of a day away. Checking every hour closes that gap to at most an hour,
            // the same granularity the rest of this system already works at.
            if (AnyEligibleAdultIsUntasked())
            {
                AssignTasks();
            }

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

            // 3.9j: a Build task's own site can finish partway through the loop above (another
            // builder's labor hour completes it) - that builder's own ProcessBuild call then clears
            // them to None right here, mid-pass, before the deferred completion event (and the
            // AssignTasks() it triggers) is delivered at the very end of this tick, well after
            // PeopleModule's own hourly system already ran and chose this hour's activity from the
            // stale None. Re-checking now, synchronously, fixes the task in time for that same
            // choice instead of leaving it to next hour.
            if (AnyEligibleAdultIsUntasked())
            {
                AssignTasks();
            }
        }

        private void ProcessBuild(EntityId personId, PersonState person, GatherTaskState task, double skillGainPerHour)
        {
            if (!task.TargetSite.HasValue || !_buildings.TryGetBuilding(task.TargetSite.Value, out BuildingState site) || site.Construction == null)
            {
                // The BuildingCompletedEventId subscription (see Initialize) re-runs AssignTasks the
                // instant this happens, so this builder picks a new task immediately, not next morning.
                PostTaskChanged(personId, task.Kind, GatherTaskKind.None, "the site finished or no longer exists");
                task.Kind = GatherTaskKind.None;
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
