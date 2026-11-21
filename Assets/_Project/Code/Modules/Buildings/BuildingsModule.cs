using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Modules.Buildings
{
    /// <summary>
    /// Building instances: construction sites that accumulate goods and
    /// labor until complete, homes and their rent, and discovered building
    /// types. Depends on map/movement (the grid and its occupancy layer),
    /// land (plots and zones), wealth (transfers, and it adds to the
    /// property-value hook) and inventory (goods delivered from an owner's
    /// stock), and registers itself as people's home-location provider.
    /// </summary>
    public sealed class BuildingsModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.buildings";

        public const string LaborHoursPerGoodUnitSettingId = "setting.buildings.labor_hours_per_good_unit";
        public const string RentPerMonthLowerSettingId = "setting.buildings.rent_per_month.lower";
        public const string RentPerMonthMiddleSettingId = "setting.buildings.rent_per_month.middle";
        public const string RentPerMonthUpperSettingId = "setting.buildings.rent_per_month.upper";
        public const string InitialStockpilePlanksSettingId = "setting.buildings.initial_stockpile_planks";
        public const string InitialStockpileStoneSettingId = "setting.buildings.initial_stockpile_stone";

        private const string PlankGoodId = "good.plank";
        private const string StoneGoodId = "good.stone";
        private const string LoafGoodId = "good.loaf";

        public const string BuildingCompletedEventId = "event.buildings.building_completed";

        public const string KeepTypeId = "building.keep";
        public const string CrownLodgingTypeId = "building.crown_lodging";
        public const string CrownStorehouseTypeId = "building.crown_storehouse";

        private const string DiscoveryQuestionCategory = "building_discovery";

        private readonly MapMovementModule _mapMovement;
        private readonly LandModule _land;
        private readonly WealthModule _wealth;
        private readonly InventoryModule _inventory;
        private readonly PeopleModule _people;

        private SettingsRegistry _settings;
        private EventBus _events;
        private EntityRegistry _entities;
        private Func<RuleRunner> _ruleRunner;
        private Func<long> _currentTick;

        private MapGrid _grid;
        private EntityTable<BuildingState> _buildings;
        private Dictionary<string, BuildingType> _buildingTypesById;

        /// <summary>A discovered type's player-editable rules: whether it's allowed, the most that may exist, and which classes may own one. Module state, not settings - it changes during play.</summary>
        private sealed class DiscoveredTypeState
        {
            public bool Allowed;
            public int MaxCount;
            public int AllowedClassesMask;
        }

        private readonly HashSet<string> _discoveryRequested = new HashSet<string>();
        private readonly Dictionary<string, DiscoveredTypeState> _discoveredTypes = new Dictionary<string, DiscoveredTypeState>();
        private readonly Dictionary<long, (string TypeId, EntityId Person)> _pendingDiscoveryQuestions = new Dictionary<long, (string TypeId, EntityId Person)>();

        /// <summary>Reverse index: a housed person's building, for O(1) home lookups from People's hourly schedule.</summary>
        private readonly Dictionary<EntityId, EntityId> _homeByPerson = new Dictionary<EntityId, EntityId>();

        /// <summary>Whether the player's first action - placing the free, instant starting stockpile - has happened yet.</summary>
        private bool _initialStockpilePlaced;

        public BuildingsModule(MapMovementModule mapMovement, LandModule land, WealthModule wealth, InventoryModule inventory, PeopleModule people)
        {
            _mapMovement = mapMovement ?? throw new ArgumentNullException(nameof(mapMovement));
            _land = land ?? throw new ArgumentNullException(nameof(land));
            _wealth = wealth ?? throw new ArgumentNullException(nameof(wealth));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _people = people ?? throw new ArgumentNullException(nameof(people));
        }

        public string Id => ModuleId;
        public int Version => 1;

        public IReadOnlyList<string> Dependencies { get; } =
            new[] { MapMovementModule.ModuleId, LandModule.ModuleId, WealthModule.ModuleId, InventoryModule.ModuleId, PeopleModule.ModuleId };

        public int SectionVersion => 1;

        public IEnumerable<KeyValuePair<EntityId, BuildingState>> Buildings => _buildings.Rows();

        public bool TryGetBuilding(EntityId id, out BuildingState building) => _buildings.TryGet(id, out building);

        public BuildingType GetBuildingType(string typeId) => _buildingTypesById.TryGetValue(typeId, out BuildingType type) ? type : null;

        /// <summary>A passable cell just outside a building's footprint, for anyone (a tenant, a worker) who needs to walk to it.</summary>
        public GridPoint? GetDoorstep(EntityId buildingId) => _buildings.TryGet(buildingId, out BuildingState building) ? FindDoorstep(building.Rect) : null;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _entities = context.Entities;
            _ruleRunner = () => context.RuleRunner;
            _currentTick = () => context.CurrentTick;

            _grid = _mapMovement.Grid;
            _buildings = new EntityTable<BuildingState>(context.Entities);
            LoadBuildingTypes(context.Content);

            _wealth.AddPropertyValueProvider(GetPropertyValue);
            _people.SetHomeLocationProvider(GetHomeLocation);
            _people.SetFoodSourceProvider(GetFoodSource);

            context.Subscribe<NewDayEvent>(OnNewDay);
            context.Subscribe<NewMonthEvent>(OnNewMonth);
            context.Subscribe(PeopleModule.PersonDiedEventId, OnPersonDied);
            _people.RegisterDeathHook(OnPersonDiedSweepGoods);
            context.Subscribe(RuleRunner.QuestionAnsweredEventId, OnQuestionAnswered);

            RegisterCommands(context);

            if (!context.IsLoading)
            {
                PlaceInitialKeep();
            }
        }

        public void Shutdown()
        {
        }

        private void LoadBuildingTypes(ContentDatabase content)
        {
            _buildingTypesById = new Dictionary<string, BuildingType>();
            foreach (ContentEntry entry in content.ListByType(BuiltInContentTypes.BuildingTypeTypeName))
            {
                var type = (BuildingType)entry.Definition;
                _buildingTypesById[type.Id] = type;
            }
        }

        private void PlaceInitialKeep()
        {
            if (!_buildingTypesById.TryGetValue(KeepTypeId, out BuildingType keep))
            {
                return;
            }

            CellRect grounds = _land.CrownGrounds;
            var origin = new GridPoint(
                grounds.X + (grounds.Width - keep.FootprintWidth) / 2,
                grounds.Y + (grounds.Height - keep.FootprintDepth) / 2);

            TryPlaceGovernmentBuilding(KeepTypeId, origin, out _, out _);
        }

        #region Placement

        /// <summary>True until the player has placed the starting stockpile (a new game's first action); always false once loaded from a save where it was already placed.</summary>
        public bool NeedsInitialStockpilePlacement => !_initialStockpilePlaced;

        /// <summary>
        /// The player's first action: places the crown storehouse for free and
        /// instantly (no construction at all), stocked with the placeholder
        /// starting planks, stone and the founders' provisions. Only the very
        /// first stockpile is free this way; a later one is requested like any
        /// other government building and costs materials normally.
        /// </summary>
        public bool TryPlaceInitialStockpile(GridPoint at, out EntityId buildingId, out string error)
        {
            buildingId = EntityId.None;

            if (_initialStockpilePlaced)
            {
                error = "The stockpile has already been placed.";
                return false;
            }

            if (!_buildingTypesById.TryGetValue(CrownStorehouseTypeId, out BuildingType type))
            {
                error = $"'{CrownStorehouseTypeId}' is not a known building type.";
                return false;
            }

            var rect = new CellRect(at.X, at.Y, type.FootprintWidth, type.FootprintDepth);
            if (!IsFreeOfBuildingsAndPlots(rect))
            {
                error = "That spot isn't free of other buildings and plots.";
                return false;
            }

            EntityId id = _entities.Create(EntityKind.Building);
            var building = new BuildingState { BuildingTypeId = type.Id, Rect = rect, Owner = null, PlotId = null, Construction = null };
            _buildings.Add(id, building);

            foreach (GridPoint cell in rect.Cells())
            {
                _grid.SetOccupied(cell, true);
            }

            int initialPlanks = (int)_settings.Get(InitialStockpilePlanksSettingId);
            if (initialPlanks > 0)
            {
                _inventory.Add(id, PlankGoodId, initialPlanks);
            }

            int initialStone = (int)_settings.Get(InitialStockpileStoneSettingId);
            if (initialStone > 0)
            {
                _inventory.Add(id, StoneGoodId, initialStone);
            }

            int loavesPerFounder = (int)_settings.Get(PeopleModule.StartingProvisionsLoavesSettingId);
            int founderCount = (int)_settings.Get(PeopleModule.FounderCountSettingId);
            if (loavesPerFounder > 0 && founderCount > 0)
            {
                _inventory.Add(id, LoafGoodId, loavesPerFounder * founderCount);
            }

            _initialStockpilePlaced = true;
            buildingId = id;
            error = null;
            return true;
        }

        public bool TryPlaceGovernmentBuilding(string typeId, GridPoint at, out EntityId buildingId, out string error)
        {
            buildingId = EntityId.None;

            if (!_buildingTypesById.TryGetValue(typeId, out BuildingType type) || !type.IsGovernment)
            {
                error = $"'{typeId}' is not a government building type.";
                return false;
            }

            var rect = new CellRect(at.X, at.Y, type.FootprintWidth, type.FootprintDepth);
            if (!IsFreeOfBuildingsAndPlots(rect))
            {
                error = "That spot isn't free of other buildings and plots.";
                return false;
            }

            buildingId = CreateBuilding(type, rect, owner: null, plotId: null);
            error = null;
            return true;
        }

        /// <summary>"requestbuilding": simulates a person's demand for a type (town growth wires the real thing in 3.11). Discovers a non-basic type the first time it's asked for.</summary>
        public bool TryRequestPrivateBuilding(EntityId person, string typeId, out EntityId buildingId, out string error)
        {
            buildingId = EntityId.None;

            if (_wealth.IsFounder(person))
            {
                error = "A crown servant owns nothing.";
                return false;
            }

            if (!_buildingTypesById.TryGetValue(typeId, out BuildingType type) || type.IsGovernment)
            {
                error = $"'{typeId}' is not a private building type.";
                return false;
            }

            if (!type.IsBasic)
            {
                if (!_discoveredTypes.ContainsKey(typeId))
                {
                    if (_discoveryRequested.Add(typeId))
                    {
                        long questionId = RaiseDiscoveryQuestion(person, type);
                        error = questionId >= 0
                            ? $"Nobody has ever wanted a {type.Name} before; asking the player."
                            : "No one is able to answer right now.";
                        return false;
                    }

                    error = $"Still waiting on a decision about '{typeId}'.";
                    return false;
                }

                if (!IsAllowedFor(typeId, person, out string disallowedReason))
                {
                    error = disallowedReason;
                    return false;
                }
            }

            if (!TryFindPlotFor(person, type, out EntityId plotId, out CellRect rect))
            {
                error = $"{person} has no suitable plot (owned or leased, right zone, free of other buildings) for a {type.Name}.";
                return false;
            }

            buildingId = CreateBuilding(type, rect, owner: person, plotId: plotId);
            error = null;
            return true;
        }

        private bool IsAllowedFor(string typeId, EntityId person, out string reason)
        {
            DiscoveredTypeState state = _discoveredTypes[typeId];

            if (!state.Allowed)
            {
                reason = $"Building type '{typeId}' is forbidden.";
                return false;
            }

            int currentCount = _buildings.Rows().Count(row => row.Value.BuildingTypeId == typeId);
            if (currentCount >= state.MaxCount)
            {
                reason = $"Building type '{typeId}' is already at its maximum of {state.MaxCount}.";
                return false;
            }

            SocialClass personClass = _wealth.GetClass(person);
            if ((state.AllowedClassesMask & (1 << (int)personClass)) == 0)
            {
                reason = $"{person}'s class may not own a '{typeId}'.";
                return false;
            }

            reason = null;
            return true;
        }

        private bool TryFindPlotFor(EntityId person, BuildingType type, out EntityId plotId, out CellRect rect)
        {
            var allowedZoneIds = new HashSet<string>(type.AllowedZones.Select(z => z.Id));

            foreach (KeyValuePair<EntityId, PlotState> row in _land.Plots)
            {
                PlotState plot = row.Value;
                if (plot.Owner != person && plot.Leaseholder != person)
                {
                    continue;
                }

                if (!allowedZoneIds.Contains(plot.ZoneId))
                {
                    continue;
                }

                if (_buildings.Rows().Any(b => b.Value.PlotId == row.Key))
                {
                    continue; // one building per plot
                }

                if (TryFindFreeSpotInside(plot.Rect, type.FootprintWidth, type.FootprintDepth, out CellRect candidate))
                {
                    plotId = row.Key;
                    rect = candidate;
                    return true;
                }
            }

            plotId = EntityId.None;
            rect = default;
            return false;
        }

        private bool TryFindFreeSpotInside(CellRect bounds, int width, int depth, out CellRect rect)
        {
            for (int y = bounds.Y; y <= bounds.Y + bounds.Height - depth; y++)
            {
                for (int x = bounds.X; x <= bounds.X + bounds.Width - width; x++)
                {
                    var candidate = new CellRect(x, y, width, depth);
                    if (IsFreeOfBuildingsAndPlots(candidate))
                    {
                        rect = candidate;
                        return true;
                    }
                }
            }

            rect = default;
            return false;
        }

        private bool IsFreeOfBuildingsAndPlots(CellRect rect)
        {
            foreach (GridPoint cell in rect.Cells())
            {
                if (!_grid.InBounds(cell) || _grid.IsOccupied(cell))
                {
                    return false;
                }
            }

            return true;
        }

        private EntityId CreateBuilding(BuildingType type, CellRect rect, EntityId? owner, EntityId? plotId)
        {
            EntityId id = _entities.Create(EntityKind.Building);
            var building = new BuildingState { BuildingTypeId = type.Id, Rect = rect, Owner = owner, PlotId = plotId, Construction = new ConstructionState() };
            _buildings.Add(id, building);

            foreach (GridPoint cell in rect.Cells())
            {
                _grid.SetOccupied(cell, true);
            }

            CheckCompletion(id, building, type, _currentTick());
            return id;
        }

        #endregion

        #region Construction

        public bool TryDeliverGoods(EntityId siteId, string goodId, int count, out string error)
        {
            if (count <= 0)
            {
                error = "Count must be positive.";
                return false;
            }

            if (!_buildings.TryGet(siteId, out BuildingState site) || site.Construction == null)
            {
                error = "No such construction site.";
                return false;
            }

            BuildingType type = _buildingTypesById[site.BuildingTypeId];
            int costIndex = -1;
            for (int i = 0; i < type.ConstructionCost.Count; i++)
            {
                if (type.ConstructionCost[i].Good.Id == goodId)
                {
                    costIndex = i;
                    break;
                }
            }

            if (costIndex < 0)
            {
                error = $"A {type.Name} doesn't need {goodId}.";
                return false;
            }

            int delivered = site.Construction.DeliveredGoods.TryGetValue(goodId, out int d) ? d : 0;
            int remaining = type.ConstructionCost[costIndex].Amount - delivered;
            if (remaining <= 0)
            {
                error = $"{goodId} has already been fully delivered.";
                return false;
            }

            int toDeliver = Math.Min(count, remaining);

            if (site.Owner.HasValue)
            {
                if (!_inventory.TryRemove(site.Owner.Value, goodId, toDeliver))
                {
                    error = $"{site.Owner.Value} doesn't have {toDeliver} {goodId}.";
                    return false;
                }
            }
            else
            {
                // A crown site draws from the crown storehouse, like any owner draws from their own
                // inventory - crown buildings are never free. The very first storehouse is placed
                // already complete (TryPlaceInitialStockpile); only a later, additional one goes
                // through this normal paid path.
                EntityId? storehouseId = FindCrownStorehouse();
                if (!storehouseId.HasValue || !_inventory.TryRemove(storehouseId.Value, goodId, toDeliver))
                {
                    error = $"The crown storehouse doesn't have {toDeliver} {goodId}.";
                    return false;
                }
            }

            site.Construction.DeliveredGoods[goodId] = delivered + toDeliver;
            CheckCompletion(siteId, site, type, _currentTick());

            error = null;
            return true;
        }

        public bool TryAddLaborHours(EntityId siteId, double hours, out string error)
        {
            if (hours <= 0)
            {
                error = "Hours must be positive.";
                return false;
            }

            if (!_buildings.TryGet(siteId, out BuildingState site) || site.Construction == null)
            {
                error = "No such construction site.";
                return false;
            }

            BuildingType type = _buildingTypesById[site.BuildingTypeId];
            double remaining = Math.Max(0.0, TotalLaborNeeded(type) - site.Construction.LaborHoursDone);
            site.Construction.LaborHoursDone += Math.Min(hours, remaining);
            CheckCompletion(siteId, site, type, _currentTick());

            error = null;
            return true;
        }

        private double TotalLaborNeeded(BuildingType type) =>
            type.ConstructionCost.Sum(c => c.Amount) * _settings.Get(LaborHoursPerGoodUnitSettingId);

        /// <summary>Total labor hours a construction site needs to finish, for the building panel's progress display; 0 for a finished building or an unknown site.</summary>
        public double GetLaborHoursNeeded(EntityId siteId) =>
            _buildings.TryGet(siteId, out BuildingState site) && site.Construction != null ? TotalLaborNeeded(_buildingTypesById[site.BuildingTypeId]) : 0.0;

        private void CheckCompletion(EntityId id, BuildingState building, BuildingType type, long tick)
        {
            if (building.Construction == null)
            {
                return;
            }

            bool goodsComplete = type.ConstructionCost.All(c =>
                building.Construction.DeliveredGoods.TryGetValue(c.Good.Id, out int delivered) && delivered >= c.Amount);
            bool laborComplete = building.Construction.LaborHoursDone >= TotalLaborNeeded(type);

            if (!goodsComplete || !laborComplete)
            {
                return;
            }

            building.Construction = null;

            if (type.HouseholdsProvided > 0)
            {
                building.Households = Enumerable.Range(0, type.HouseholdsProvided).Select(_ => new HouseholdState()).ToList();

                // Only move the owner in if they actually need a home; an owner who already has one elsewhere
                // (building this as a rental) leaves the household vacant for the normal daily rental process.
                if (building.Owner.HasValue && _people.IsHomeless(building.Owner.Value))
                {
                    HouseholdState ownerHome = building.Households[0];
                    ownerHome.Occupant = building.Owner;
                    ownerHome.IsOwnerOccupied = true;
                    _homeByPerson[building.Owner.Value] = id;
                }
            }

            _events.Post(BuildingCompletedEventId, tick, new EventData().SetEntity("building", id).SetString("buildingType", type.Id));
        }

        #endregion

        #region Homes and rent

        private GridPoint? GetHomeLocation(EntityId person)
        {
            if (_homeByPerson.TryGetValue(person, out EntityId buildingId) && _buildings.TryGet(buildingId, out BuildingState building))
            {
                return FindDoorstep(building.Rect);
            }

            return null;
        }

        /// <summary>Crown lodging and the keep (built free, at no cost) both house people for free; everything else charges rent.</summary>
        private static bool IsFreeLodging(string buildingTypeId) => buildingTypeId == CrownLodgingTypeId || buildingTypeId == KeepTypeId;

        /// <summary>Founders and the monarch eat free from the crown storehouse, once one exists; everyone else eats from their own inventory (People's default).</summary>
        private EntityId? GetFoodSource(EntityId person)
        {
            if ((_people.IsFounder(person) || _people.IsMonarch(person)) && TryFindCrownStorehouse(out EntityId storehouseId))
            {
                return storehouseId;
            }

            return null;
        }

        /// <summary>A passable cell just outside the building's footprint. The footprint itself is marked occupied, so pathfinding could never reach it as a goal.</summary>
        private GridPoint? FindDoorstep(CellRect rect)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                var above = new GridPoint(x, rect.Y - 1);
                if (_grid.IsPassable(above))
                {
                    return above;
                }

                var below = new GridPoint(x, rect.Y + rect.Height);
                if (_grid.IsPassable(below))
                {
                    return below;
                }
            }

            for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            {
                var left = new GridPoint(rect.X - 1, y);
                if (_grid.IsPassable(left))
                {
                    return left;
                }

                var right = new GridPoint(rect.X + rect.Width, y);
                if (_grid.IsPassable(right))
                {
                    return right;
                }
            }

            return rect.Center; // no free doorstep found; fall back to the footprint itself rather than leaving the person stuck with no destination at all
        }

        private Money RentForClass(SocialClass homeClass)
        {
            string settingId = homeClass switch
            {
                SocialClass.Upper => RentPerMonthUpperSettingId,
                SocialClass.Middle => RentPerMonthMiddleSettingId,
                _ => RentPerMonthLowerSettingId
            };

            return Money.FromShil(_settings.Get(settingId));
        }

        private void OnNewDay(NewDayEvent evt)
        {
            foreach (KeyValuePair<EntityId, PersonState> row in _people.People.ToList())
            {
                EntityId personId = row.Key;
                if (!_people.IsHomeless(personId))
                {
                    continue;
                }

                FindAndRentCheapestHomeFor(personId);
            }
        }

        private void FindAndRentCheapestHomeFor(EntityId personId)
        {
            SocialClass personClass = _wealth.GetClass(personId);
            Money balance = _wealth.GetBalance(AccountId.Person(personId));

            EntityId bestBuilding = EntityId.None;
            int bestHouseholdIndex = -1;
            Money bestRent = default;
            bool found = false;

            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                BuildingState building = row.Value;
                if (building.Construction != null || building.Households.Count == 0)
                {
                    continue;
                }

                // The keep houses only founders and the monarch for free; everyone else competes for
                // ordinary housing (crown lodging included) like any citizen.
                if (building.BuildingTypeId == KeepTypeId && !_people.IsFounder(personId) && !_people.IsMonarch(personId))
                {
                    continue;
                }

                BuildingType type = _buildingTypesById[building.BuildingTypeId];
                if (!type.HomeClass.HasValue || type.HomeClass.Value > personClass)
                {
                    continue;
                }

                Money rent = IsFreeLodging(building.BuildingTypeId) ? Money.Zero : RentForClass(type.HomeClass.Value);
                if (rent.HundredthsOfShil > balance.HundredthsOfShil)
                {
                    continue;
                }

                for (int i = 0; i < building.Households.Count; i++)
                {
                    if (building.Households[i].Occupant.HasValue)
                    {
                        continue;
                    }

                    if (!found || rent.HundredthsOfShil < bestRent.HundredthsOfShil)
                    {
                        found = true;
                        bestBuilding = row.Key;
                        bestHouseholdIndex = i;
                        bestRent = rent;
                    }

                    break; // one vacancy per building is enough to consider it
                }
            }

            if (!found)
            {
                return;
            }

            _buildings.TryGet(bestBuilding, out BuildingState chosen);
            bool freeLodging = IsFreeLodging(chosen.BuildingTypeId);
            AccountId landlord = chosen.Owner.HasValue ? AccountId.Person(chosen.Owner.Value) : AccountId.Crown;

            if (freeLodging || _wealth.TryTransfer(AccountId.Person(personId), landlord, bestRent, TransferReason.Rent))
            {
                chosen.Households[bestHouseholdIndex].Occupant = personId;
                _homeByPerson[personId] = bestBuilding;
            }
        }

        private void OnNewMonth(NewMonthEvent evt)
        {
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                BuildingState building = row.Value;
                if (building.Construction != null || building.Households.Count == 0 || IsFreeLodging(building.BuildingTypeId))
                {
                    continue; // crown lodging and the keep charge no rent, so are never evicted
                }

                BuildingType type = _buildingTypesById[building.BuildingTypeId];
                Money rent = RentForClass(type.HomeClass.Value);
                AccountId landlord = building.Owner.HasValue ? AccountId.Person(building.Owner.Value) : AccountId.Crown;

                foreach (HouseholdState household in building.Households)
                {
                    if (!household.Occupant.HasValue || household.IsOwnerOccupied)
                    {
                        continue;
                    }

                    if (!_wealth.TryTransfer(AccountId.Person(household.Occupant.Value), landlord, rent, TransferReason.Rent))
                    {
                        _homeByPerson.Remove(household.Occupant.Value);
                        household.Occupant = null;
                    }
                }
            }
        }

        /// <summary>Property of the dead: their buildings return to the crown (tenants stay, now renting from the crown), and their own household slot (owned or rented) is vacated.</summary>
        private void OnPersonDied(PostedEvent posted)
        {
            EntityId person = posted.Data.GetEntity("person");

            if (_homeByPerson.TryGetValue(person, out EntityId homeBuildingId) && _buildings.TryGet(homeBuildingId, out BuildingState home))
            {
                foreach (HouseholdState household in home.Households)
                {
                    if (household.Occupant == person)
                    {
                        household.Occupant = null;
                        household.IsOwnerOccupied = false;
                        break;
                    }
                }

                _homeByPerson.Remove(person);
            }

            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                if (row.Value.Owner == person)
                {
                    row.Value.Owner = null;
                }
            }
        }

        /// <summary>
        /// Property of the dead: their goods go to the crown storehouse, if one exists yet. Run
        /// synchronously before the person is destroyed - see PeopleModule.RegisterDeathHook. Unlike
        /// OnPersonDied above, this can't wait for the deferred event: by the time it's delivered,
        /// EntityRegistry.Destroy has already scrubbed the dead person's own inventory row.
        /// </summary>
        private void OnPersonDiedSweepGoods(EntityId person)
        {
            EntityId? storehouseId = FindCrownStorehouse();
            if (storehouseId.HasValue)
            {
                foreach (KeyValuePair<string, int> good in _inventory.GetAll(person).ToList())
                {
                    if (_inventory.TryRemove(person, good.Key, good.Value))
                    {
                        _inventory.Add(storehouseId.Value, good.Key, good.Value);
                    }
                }
            }
        }

        private EntityId? FindCrownStorehouse()
        {
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                if (row.Value.BuildingTypeId == CrownStorehouseTypeId && row.Value.Construction == null)
                {
                    return row.Key;
                }
            }

            return null;
        }

        /// <summary>The crown's own goods reserve, if a complete one exists yet. Market's crown construction sites take materials from here first.</summary>
        public bool TryFindCrownStorehouse(out EntityId storehouseId)
        {
            EntityId? found = FindCrownStorehouse();
            storehouseId = found ?? EntityId.None;
            return found.HasValue;
        }

        #endregion

        #region Property value

        private Money GetPropertyValue(EntityId person)
        {
            Money total = Money.Zero;
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                BuildingState building = row.Value;
                if (building.Owner != person)
                {
                    continue;
                }

                BuildingType type = _buildingTypesById[building.BuildingTypeId];
                total += building.Construction == null ? FullConstructionValue(type) : DeliveredConstructionValue(building.Construction, type);
            }

            return total;
        }

        private static Money FullConstructionValue(BuildingType type) =>
            type.ConstructionCost.Aggregate(Money.Zero, (sum, cost) => sum + GoodValue(cost.Good.Id, cost.Amount));

        private static Money DeliveredConstructionValue(ConstructionState construction, BuildingType type)
        {
            Money sum = Money.Zero;
            foreach (GoodAmount cost in type.ConstructionCost)
            {
                int delivered = construction.DeliveredGoods.TryGetValue(cost.Good.Id, out int d) ? d : 0;
                sum += GoodValue(cost.Good.Id, Math.Min(delivered, cost.Amount));
            }

            return sum;
        }

        /// <summary>Placeholder flat prices, matching the Phase 3 update's base prices; a real market (3.9) will price goods dynamically.</summary>
        private static Money GoodValue(string goodId, int amount)
        {
            double perUnit = goodId switch
            {
                "good.plank" => 2.0,
                "good.stone" => 2.5,
                "good.block" => 10.0,
                "good.furniture" => 40.0,
                "good.log" => 2.5,
                "good.wheat" => 0.5,
                "good.flour" => 3.0,
                "good.loaf" => 2.0,
                _ => 0.0
            };

            return Money.FromShil(perUnit * amount);
        }

        #endregion

        #region Discovery

        private long RaiseDiscoveryQuestion(EntityId person, BuildingType type)
        {
            RuleRunner runner = _ruleRunner();
            if (runner == null)
            {
                return -1;
            }

            long questionId = runner.AskQuestion(
                DiscoveryQuestionCategory,
                "New building type",
                $"Someone wants to build a {type.Name}. Allow it?",
                new[] { "Allow", "Forbid" },
                defaultChoiceNumber: 2);

            _pendingDiscoveryQuestions[questionId] = (type.Id, person);
            return questionId;
        }

        private void OnQuestionAnswered(PostedEvent posted)
        {
            if (posted.Data.GetString("category") != DiscoveryQuestionCategory)
            {
                return;
            }

            long questionId = (long)posted.Data.GetDouble("questionId");
            if (!_pendingDiscoveryQuestions.TryGetValue(questionId, out (string TypeId, EntityId Person) pending))
            {
                return;
            }

            _pendingDiscoveryQuestions.Remove(questionId);

            bool allowed = (int)posted.Data.GetDouble("choice") == 1;
            RecordDiscoveryDecision(pending.TypeId, allowed);

            if (allowed)
            {
                TryRequestPrivateBuilding(pending.Person, pending.TypeId, out _, out _);
            }
        }

        private void RecordDiscoveryDecision(string typeId, bool allowed)
        {
            if (_discoveredTypes.ContainsKey(typeId))
            {
                return;
            }

            _discoveredTypes[typeId] = new DiscoveredTypeState { Allowed = allowed, MaxCount = allowed ? 999 : 0, AllowedClassesMask = 7 };
        }

        /// <summary>The player (or a minister) changing a discovered type's rules. Applied next tick: it only changes stored state, read fresh by the next request.</summary>
        public bool TrySetDiscoveredTypeConfig(string typeId, bool? allowed, int? maxCount, int? allowedClassesMask, out string error)
        {
            if (!_discoveredTypes.TryGetValue(typeId, out DiscoveredTypeState state))
            {
                error = $"'{typeId}' hasn't been discovered yet.";
                return false;
            }

            if (allowed.HasValue)
            {
                state.Allowed = allowed.Value;
            }

            if (maxCount.HasValue)
            {
                state.MaxCount = maxCount.Value;
            }

            if (allowedClassesMask.HasValue)
            {
                state.AllowedClassesMask = allowedClassesMask.Value;
            }

            error = null;
            return true;
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("buildings", "buildings - lists every building and site.", args =>
            {
                List<string> lines = _buildings.Rows()
                    .Select(row => $"{row.Key}: {row.Value.BuildingTypeId} at ({row.Value.Rect.X},{row.Value.Rect.Y}) " +
                                   (row.Value.Construction != null ? "(under construction)" : "(complete)"))
                    .ToList();

                return lines.Count == 0 ? "No buildings." : string.Join("\n", lines);
            }));

            context.RegisterCommand(new ConsoleCommand("building", "building <id> - shows one building in full.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !_buildings.Has(id))
                {
                    return "Usage: building <id> (e.g. e12.3)";
                }

                return string.Join("\n", Inspect(id));
            }));

            context.RegisterCommand(new ConsoleCommand("site", "site <id> - shows one construction site in full.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !_buildings.TryGet(id, out BuildingState building) || building.Construction == null)
                {
                    return "Usage: site <id> (e.g. e12.3)";
                }

                return string.Join("\n", Inspect(id));
            }));

            context.RegisterCommand(new ConsoleCommand("placestockpile", "placestockpile <x> <y> - the player's first action: places the free, instant starting stockpile.", args =>
            {
                if (args.Length != 2 || !ConsoleArgs.TryParseInt(args[0], out int x) || !ConsoleArgs.TryParseInt(args[1], out int y))
                {
                    return "Usage: placestockpile <x> <y>";
                }

                return TryPlaceInitialStockpile(new GridPoint(x, y), out EntityId id, out string error)
                    ? $"Placed the stockpile at ({x},{y}) ({id})."
                    : error;
            }));

            context.RegisterCommand(new ConsoleCommand("requestbuilding", "requestbuilding <personId> <buildingType> - simulates demand for a private building.", args =>
            {
                if (args.Length != 2 || !EntityId.TryParse(args[0], out EntityId person))
                {
                    return "Usage: requestbuilding <personId> <buildingType>";
                }

                return TryRequestPrivateBuilding(person, args[1], out EntityId id, out string error)
                    ? $"Started construction of {args[1]} ({id})."
                    : error;
            }));

            // Normal play no longer needs this: Market's bulk orders (3.9) now source and deliver
            // construction materials automatically. Kept for tests and debugging.
            context.RegisterCommand(new ConsoleCommand("deliver", "deliver <siteId> <good> <count> - delivers goods to a construction site, from the owner's inventory (or the crown storehouse) (test/debug only).", args =>
            {
                if (args.Length != 3 || !EntityId.TryParse(args[0], out EntityId siteId) || !ConsoleArgs.TryParseInt(args[2], out int count))
                {
                    return "Usage: deliver <siteId> <good> <count>";
                }

                return TryDeliverGoods(siteId, args[1], count, out string error) ? $"Delivered {count} {args[1]} to {siteId}." : error;
            }));

            // Normal play no longer needs this: hired builders (Jobs, 3.8) add real labor automatically. Kept for tests and debugging.
            context.RegisterCommand(new ConsoleCommand("buildwork", "buildwork <siteId> <hours> - adds labor hours to a construction site (test/debug only).", args =>
            {
                if (args.Length != 2 || !EntityId.TryParse(args[0], out EntityId siteId) || !ConsoleArgs.TryParseDouble(args[1], out double hours))
                {
                    return "Usage: buildwork <siteId> <hours>";
                }

                return TryAddLaborHours(siteId, hours, out string error) ? $"Added {hours} labor hours to {siteId}." : error;
            }));
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteBool(_initialStockpilePlaced);

            writer.WriteInt(_buildings.Count);
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                writer.WriteEntityId(row.Key);
                BuildingState building = row.Value;

                writer.WriteString(building.BuildingTypeId);
                writer.WriteInt(building.Rect.X);
                writer.WriteInt(building.Rect.Y);
                writer.WriteInt(building.Rect.Width);
                writer.WriteInt(building.Rect.Height);

                writer.WriteBool(building.Owner.HasValue);
                if (building.Owner.HasValue)
                {
                    writer.WriteEntityId(building.Owner.Value);
                }

                writer.WriteBool(building.PlotId.HasValue);
                if (building.PlotId.HasValue)
                {
                    writer.WriteEntityId(building.PlotId.Value);
                }

                writer.WriteBool(building.Construction != null);
                if (building.Construction != null)
                {
                    writer.WriteStringList(building.Construction.DeliveredGoods.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
                    foreach (string goodId in building.Construction.DeliveredGoods.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        writer.WriteInt(building.Construction.DeliveredGoods[goodId]);
                    }

                    writer.WriteDouble(building.Construction.LaborHoursDone);
                }

                writer.WriteInt(building.Households.Count);
                foreach (HouseholdState household in building.Households)
                {
                    writer.WriteBool(household.Occupant.HasValue);
                    if (household.Occupant.HasValue)
                    {
                        writer.WriteEntityId(household.Occupant.Value);
                    }

                    writer.WriteBool(household.IsOwnerOccupied);
                }
            }

            writer.WriteStringList(_discoveryRequested.OrderBy(s => s, StringComparer.Ordinal).ToList());

            List<string> decidedTypeIds = _discoveredTypes.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
            writer.WriteInt(decidedTypeIds.Count);
            foreach (string typeId in decidedTypeIds)
            {
                DiscoveredTypeState state = _discoveredTypes[typeId];
                writer.WriteString(typeId);
                writer.WriteBool(state.Allowed);
                writer.WriteInt(state.MaxCount);
                writer.WriteInt(state.AllowedClassesMask);
            }

            writer.WriteInt(_pendingDiscoveryQuestions.Count);
            foreach (KeyValuePair<long, (string TypeId, EntityId Person)> entry in _pendingDiscoveryQuestions.OrderBy(e => e.Key))
            {
                writer.WriteLong(entry.Key);
                writer.WriteString(entry.Value.TypeId);
                writer.WriteEntityId(entry.Value.Person);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            _initialStockpilePlaced = reader.ReadBool();

            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var building = new BuildingState
                {
                    BuildingTypeId = reader.ReadString(),
                    Rect = new CellRect(reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt())
                };

                if (reader.ReadBool())
                {
                    building.Owner = reader.ReadEntityId();
                }

                if (reader.ReadBool())
                {
                    building.PlotId = reader.ReadEntityId();
                }

                if (reader.ReadBool())
                {
                    building.Construction = new ConstructionState();
                    List<string> goodIds = reader.ReadStringList();
                    foreach (string goodId in goodIds)
                    {
                        building.Construction.DeliveredGoods[goodId] = reader.ReadInt();
                    }

                    building.Construction.LaborHoursDone = reader.ReadDouble();
                }

                int householdCount = reader.ReadInt();
                building.Households = new List<HouseholdState>(householdCount);
                for (int h = 0; h < householdCount; h++)
                {
                    var household = new HouseholdState();
                    if (reader.ReadBool())
                    {
                        household.Occupant = reader.ReadEntityId();
                    }

                    household.IsOwnerOccupied = reader.ReadBool();
                    building.Households.Add(household);

                    if (household.Occupant.HasValue)
                    {
                        _homeByPerson[household.Occupant.Value] = id;
                    }
                }

                _buildings.Add(id, building);
                foreach (GridPoint cell in building.Rect.Cells())
                {
                    _grid.SetOccupied(cell, true);
                }
            }

            foreach (string typeId in reader.ReadStringList())
            {
                _discoveryRequested.Add(typeId);
            }

            int decidedCount = reader.ReadInt();
            for (int i = 0; i < decidedCount; i++)
            {
                string typeId = reader.ReadString();
                _discoveredTypes[typeId] = new DiscoveredTypeState
                {
                    Allowed = reader.ReadBool(),
                    MaxCount = reader.ReadInt(),
                    AllowedClassesMask = reader.ReadInt()
                };
            }

            int pendingCount = reader.ReadInt();
            for (int i = 0; i < pendingCount; i++)
            {
                long questionId = reader.ReadLong();
                string typeId = reader.ReadString();
                EntityId person = reader.ReadEntityId();
                _pendingDiscoveryQuestions[questionId] = (typeId, person);
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (!_buildings.TryGet(entity, out BuildingState building))
            {
                yield break;
            }

            BuildingType type = _buildingTypesById[building.BuildingTypeId];
            yield return $"{type.Name} at ({building.Rect.X},{building.Rect.Y}) {building.Rect.Width}x{building.Rect.Height}, owner: {(building.Owner.HasValue ? building.Owner.Value.ToString() : "crown")}";

            if (building.Construction != null)
            {
                double totalGoods = type.ConstructionCost.Sum(c => c.Amount);
                double deliveredGoods = type.ConstructionCost.Sum(c => Math.Min(c.Amount, building.Construction.DeliveredGoods.TryGetValue(c.Good.Id, out int d) ? d : 0));
                double laborNeeded = TotalLaborNeeded(type);
                double progress = totalGoods + laborNeeded > 0 ? (deliveredGoods + building.Construction.LaborHoursDone) / (totalGoods + laborNeeded) * 100.0 : 100.0;
                yield return $"Under construction: {progress:F0}% (goods {deliveredGoods:F0}/{totalGoods:F0}, labor {building.Construction.LaborHoursDone:F0}/{laborNeeded:F0} hours)";
            }
            else
            {
                yield return "Complete.";
            }

            for (int i = 0; i < building.Households.Count; i++)
            {
                HouseholdState household = building.Households[i];
                string status = household.Occupant.HasValue
                    ? household.IsOwnerOccupied ? $"owner-occupied by {household.Occupant.Value}" : $"rented by {household.Occupant.Value}"
                    : "vacant";
                yield return $"  Household {i}: {status}";
            }
        }

        #endregion
    }
}
