using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Ponds;

namespace TheEmptying.Modules.Zones
{
    /// <summary>R21: the result of DeleteZone - whether the whole zone was removed, or (if anything was occupied) how many occupied spots it still has, kept until the player deletes it again once they're empty.</summary>
    public readonly struct ZoneDeleteResult
    {
        public bool FullyDeleted { get; }
        public int RemainingOccupiedSpots { get; }

        public ZoneDeleteResult(bool fullyDeleted, int remainingOccupiedSpots)
        {
            FullyDeleted = fullyDeleted;
            RemainingOccupiedSpots = remainingOccupiedSpots;
        }
    }

    /// <summary>
    /// R18b: placed zones - sets of cells (which may form several
    /// separate pieces), each with its own name and color. Paint can
    /// never claim a cell already in another zone, a crown building's
    /// footprint, or pond water, but may cover trees, stones and grub
    /// mounds. R20: a stockpile zone also gets a fixed list of 2x2 m
    /// stack spots, laid out once (from the world seed) when it's
    /// created, in one of two layouts (Uniform or Natural). R22: each
    /// spot splits into four 1x1 m quarters and can hold several piles
    /// (mixing), each of one item, covering one or more quarters
    /// depending on that item's PileShape. R21: a zone can be modified
    /// (type, name, color, cells - ApplyZoneEdit) or deleted (DeleteZone),
    /// but never loses a cell a spot still holds anything in - see the
    /// Occupancy region. Depends on Map (cell size, cell/world
    /// conversions), Buildings (the crown-building check) and Ponds (the
    /// pond-water check).
    /// </summary>
    public sealed class ZonesModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.zones";
        public const string StockpileTypeId = "zone.stockpile";
        public const string LivingAreaTypeId = "zone.living_area";

        public const string LayoutUniformId = "layout.uniform";
        public const string LayoutNaturalId = "layout.natural";

        /// <summary>R20: a stack spot's side length in meters, placeholder.</summary>
        public const string SpotSizeMetersSettingId = "setting.zones.stockpile_spot_size_meters";

        /// <summary>R22: how many size-1 capacity units one quarter (a quarter of a spot) holds, placeholder.</summary>
        public const string CapacityUnitsPerQuarterSettingId = "setting.zones.stockpile_capacity_units_per_quarter";

        public const string NaturalPatchMinSpotsPerSideSettingId = "setting.zones.stockpile_natural_patch_min_spots_per_side";
        public const string NaturalPatchMaxSpotsPerSideSettingId = "setting.zones.stockpile_natural_patch_max_spots_per_side";
        public const string NaturalPatchMinAngleDegreesSettingId = "setting.zones.stockpile_natural_patch_min_angle_degrees";
        public const string NaturalPatchMaxAngleDegreesSettingId = "setting.zones.stockpile_natural_patch_max_angle_degrees";
        public const string NaturalMaxPlacementAttemptsSettingId = "setting.zones.stockpile_natural_max_placement_attempts";

        /// <summary>R14: posted whenever an item is put in a stockpile - feeds the discovery system.</summary>
        private const string ItemStoredEventId = "event.zones.item_stored";

        /// <summary>R15b: posted whenever a zone is placed - feeds the discovery system.</summary>
        private const string ZonePlacedEventId = "event.zones.placed";

        private readonly MapModule _mapModule;
        private readonly BuildingsModule _buildingsModule;
        private readonly PondsModule _pondsModule;
        private EntityRegistry _entities;
        private ContentDatabase _content;
        private SettingsRegistry _settings;
        private RandomService _randomService;
        private EntityTable<ZoneState> _zones;
        private ModuleContext _context;

        public ZonesModule(MapModule mapModule, BuildingsModule buildingsModule, PondsModule pondsModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
            _buildingsModule = buildingsModule ?? throw new ArgumentNullException(nameof(buildingsModule));
            _pondsModule = pondsModule ?? throw new ArgumentNullException(nameof(pondsModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public int SectionVersion => 4;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId, BuildingsModule.ModuleId, PondsModule.ModuleId };

        public IEnumerable<KeyValuePair<EntityId, ZoneState>> Zones => _zones.Rows();

        public IEnumerable<KeyValuePair<EntityId, ZoneState>> LivingAreas => _zones.Rows().Where(r => r.Value.ZoneTypeId == LivingAreaTypeId);

        public IEnumerable<KeyValuePair<EntityId, ZoneState>> Stockpiles => _zones.Rows().Where(r => r.Value.ZoneTypeId == StockpileTypeId);

        public bool TryGetZone(EntityId id, out ZoneState zone) => _zones.TryGet(id, out zone);

        /// <summary>Every existing zone's name - for the zoning menu's default-naming rule.</summary>
        public IEnumerable<string> ZoneNames => _zones.Rows().Select(r => r.Value.Name).Where(n => n != null);

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _content = context.Content;
            _settings = context.Settings;
            _randomService = context.RandomService;
            _context = context;
            _zones = new EntityTable<ZoneState>(_entities);
        }

        public void Shutdown()
        {
        }

        /// <summary>
        /// Whether this cell is free to paint right now: not already in
        /// any zone (optionally excluding the zone currently being
        /// redrawn, once that exists), not inside a crown building's
        /// footprint, and not pond water. Trees, stones and grub mounds
        /// never block paint.
        /// </summary>
        public bool CanPaintCell(CellCoord cell, EntityId? excludeZoneId = null)
        {
            foreach (KeyValuePair<EntityId, ZoneState> row in _zones.Rows())
            {
                if (excludeZoneId.HasValue && row.Key == excludeZoneId.Value)
                {
                    continue;
                }

                if (row.Value.Cells.Contains(cell))
                {
                    return false;
                }
            }

            foreach (KeyValuePair<EntityId, BuildingState> row in _buildingsModule.Buildings)
            {
                if (row.Value.CoversCell(cell))
                {
                    return false;
                }
            }

            if (_pondsModule.TryGetPondAt(cell, out _))
            {
                return false;
            }

            return true;
        }

        /// <summary>The zone (if any) whose painted cells include this cell - the world click router's zone-selection fallback.</summary>
        public bool TryGetZoneAtCell(CellCoord cell, out EntityId zoneId)
        {
            foreach (KeyValuePair<EntityId, ZoneState> row in _zones.Rows())
            {
                if (row.Value.Cells.Contains(cell))
                {
                    zoneId = row.Key;
                    return true;
                }
            }

            zoneId = default;
            return false;
        }

        /// <summary>
        /// Creates a new zone from a final painted cell set (the Create
        /// button's Yes). Only cells CanPaintCell still allows are kept,
        /// so this stays honest even if called with a stale set. For a
        /// stockpile, lays out its fixed stack spots from the given
        /// layout (LayoutUniformId or LayoutNaturalId; anything else,
        /// including null, means LayoutNaturalId - the default), seeded
        /// from the new zone's own entity id so the same zone always
        /// gets the same spots.
        /// </summary>
        public EntityId CreateZone(string zoneTypeId, string name, string colorHex, IEnumerable<CellCoord> cells, string layoutId = null)
        {
            var state = new ZoneState { ZoneTypeId = zoneTypeId, Name = name, ColorHex = colorHex };
            foreach (CellCoord cell in cells)
            {
                if (CanPaintCell(cell))
                {
                    state.Cells.Add(cell);
                }
            }

            RecomputeCentroid(state);

            EntityId id = _entities.Create(EntityKind.Zone);

            if (zoneTypeId == StockpileTypeId)
            {
                state.LayoutId = layoutId == LayoutUniformId ? LayoutUniformId : LayoutNaturalId;
                GenerateSpots(id, state);
            }

            _zones.Add(id, state);
            _context.Events.Post(ZonePlacedEventId, _context.CurrentTick, new EventData().SetString("contentId", zoneTypeId));
            return id;
        }

        /// <summary>How many people (from the given current set) fall within this zone's painted cells right now. Takes the set as a parameter - this module never reads PeopleModule, so the dependency only ever runs the other way.</summary>
        public int CountPeopleInside(EntityId zoneId, IEnumerable<(double WorldX, double WorldZ)> peoplePositions)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return 0;
            }

            return peoplePositions.Count(p => zone.Cells.Contains(_mapModule.WorldToCell(p.WorldX, p.WorldZ)));
        }

        private void RecomputeCentroid(ZoneState state)
        {
            if (state.Cells.Count == 0)
            {
                state.CenterX = 0;
                state.CenterZ = 0;
                return;
            }

            double cellSize = _mapModule.CellSizeMeters;
            double sumX = 0;
            double sumZ = 0;
            foreach (CellCoord cell in state.Cells)
            {
                sumX += (cell.X + 0.5) * cellSize;
                sumZ += (cell.Z + 0.5) * cellSize;
            }

            state.CenterX = sumX / state.Cells.Count;
            state.CenterZ = sumZ / state.Cells.Count;
        }

        #region Stockpile

        private void GenerateSpots(EntityId zoneId, ZoneState state)
        {
            double spotSize = _settings.Get(SpotSizeMetersSettingId);
            SimRandom random = _randomService.GetStream($"zones.layout.{zoneId}");

            List<StockSpot> spots = StockSpotLayoutGenerator.Generate(
                state.LayoutId,
                state.Cells,
                _mapModule.CellSizeMeters,
                spotSize,
                (int)_settings.Get(NaturalPatchMinSpotsPerSideSettingId),
                (int)_settings.Get(NaturalPatchMaxSpotsPerSideSettingId),
                _settings.Get(NaturalPatchMinAngleDegreesSettingId),
                _settings.Get(NaturalPatchMaxAngleDegreesSettingId),
                (int)_settings.Get(NaturalMaxPlacementAttemptsSettingId),
                random);

            state.Spots.AddRange(spots);
        }

        /// <summary>R22: 25 (placeholder) capacity units per quarter this pile shape covers, divided by the item's size, rounded down, minimum 1 - how much of this item one pile of this shape can hold.</summary>
        public int CapacityPerPile(PileShape shape, int itemSize)
        {
            int unitsPerQuarter = (int)_settings.Get(CapacityUnitsPerQuarterSettingId);
            return Math.Max(1, (unitsPerQuarter * shape.QuarterCount()) / Math.Max(1, itemSize));
        }

        /// <summary>The live view of one item type at one stockpile - amount stored (summed across every pile of it), its keep-on-hand limit (defaulted if never overridden), and whether it's blocked.</summary>
        public (int Amount, int Limit, bool Blocked) GetStockpileItemInfo(EntityId zoneId, string itemContentId)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return (0, 0, false);
            }

            int amount = AmountStoredOf(zone, itemContentId);
            int defaultLimit = DefaultLimitFor(zone, itemContentId);

            if (zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry entry))
            {
                return (amount, entry.KeepOnHandLimitOverride ?? defaultLimit, entry.Blocked);
            }

            return (amount, defaultLimit, false);
        }

        public void SetKeepOnHandLimit(EntityId zoneId, string itemContentId, int limit)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return;
            }

            GetOrCreateEntry(zone, itemContentId).KeepOnHandLimitOverride = Math.Max(0, limit);
        }

        public void SetBlocked(EntityId zoneId, string itemContentId, bool blocked)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return;
            }

            GetOrCreateEntry(zone, itemContentId).Blocked = blocked;
        }

        /// <summary>R13/R20/R22: removes up to amount of this item, taking from the least-full pile first (anywhere in the stockpile) and spilling into the next least-full pile if that one empties before amount is satisfied; returns how much was actually removed, never more than what's stored. An emptied pile is removed, freeing its quarters for the next pile.</summary>
        public int RemoveItemFromStockpile(EntityId zoneId, string itemContentId, int amount)
        {
            if (amount <= 0 || !_zones.TryGet(zoneId, out ZoneState zone))
            {
                return 0;
            }

            int remaining = amount;
            int removed = 0;

            while (remaining > 0)
            {
                (StockSpot spot, StockPile pile) = FindLeastFullPile(zone, itemContentId);
                if (pile == null)
                {
                    break;
                }

                int takeNow = Math.Min(remaining, pile.AmountStored);
                pile.AmountStored -= takeNow;
                remaining -= takeNow;
                removed += takeNow;

                if (pile.AmountStored == 0)
                {
                    spot.Piles.Remove(pile);
                }
            }

            return removed;
        }

        /// <summary>
        /// R22: adds as much of amountRequested as piles, quarters, sizes,
        /// blocks and the keep-on-hand limit allow: tops up any pile of
        /// this item with room first, then starts a new pile - in a spot
        /// that already holds this item with free quarters for its shape,
        /// else the nearest spot already partly used by other items with
        /// free quarters, else the empty spot nearest this item's other
        /// piles (any empty spot if it has none) - spilling into the next
        /// pile the same way if one fills partway through. Added may be
        /// less than requested. Reason is null when everything fit.
        /// </summary>
        public (int Added, int DidNotFit, string Reason) TryAddItemToStockpile(EntityId zoneId, string itemContentId, int amountRequested)
        {
            if (amountRequested <= 0)
            {
                return (0, 0, null);
            }

            if (!_zones.TryGet(zoneId, out ZoneState zone) || zone.ZoneTypeId != StockpileTypeId)
            {
                return (0, amountRequested, "Not a stockpile.");
            }

            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return (0, amountRequested, "Unknown item.");
            }

            bool blocked = zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry blockEntry) && blockEntry.Blocked;
            if (blocked)
            {
                return (0, amountRequested, "Blocked here.");
            }

            int capacityPerPile = CapacityPerPile(item.PileShape, item.Size);
            int defaultLimit = DefaultLimitFor(zone, itemContentId);
            int limit = zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry limitEntry) ? limitEntry.KeepOnHandLimitOverride ?? defaultLimit : defaultLimit;
            int currentAmount = AmountStoredOf(zone, itemContentId);

            int remaining = amountRequested;
            int added = 0;
            bool hitLimit = false;

            while (remaining > 0)
            {
                int limitRoom = Math.Max(0, limit - (currentAmount + added));
                if (limitRoom <= 0)
                {
                    hitLimit = true;
                    break;
                }

                (StockSpot existingSpot, StockPile existingPile) = FindPileWithRoom(zone, itemContentId, capacityPerPile);
                if (existingPile != null)
                {
                    int room = capacityPerPile - existingPile.AmountStored;
                    int addNow = Math.Min(remaining, Math.Min(room, limitRoom));
                    if (addNow <= 0)
                    {
                        break;
                    }

                    existingPile.AmountStored += addNow;
                    remaining -= addNow;
                    added += addNow;
                    continue;
                }

                if (!TryFindSpotForNewPile(zone, itemContentId, item.PileShape, out StockSpot newSpot, out List<int> quarters))
                {
                    break;
                }

                int addNowNew = Math.Min(remaining, Math.Min(capacityPerPile, limitRoom));
                if (addNowNew <= 0)
                {
                    break;
                }

                var newPile = new StockPile { ItemContentId = itemContentId, StackModelIndex = PickStackModelIndex(zoneId, item), AmountStored = addNowNew };
                newPile.QuarterIndices.AddRange(quarters);
                newSpot.Piles.Add(newPile);

                remaining -= addNowNew;
                added += addNowNew;
            }

            int didNotFit = remaining;
            if (added > 0)
            {
                _context.Events.Post(ItemStoredEventId, _context.CurrentTick, new EventData().SetString("contentId", itemContentId));
            }

            string reason = didNotFit <= 0 ? null : hitLimit ? "Hit the keep-on-hand limit." : "Not enough room left.";
            return (added, didNotFit, reason);
        }

        /// <summary>
        /// R13/R20/R22: how many more of this item this stockpile could
        /// currently take across every spot - the same capacity math
        /// TryAddItemToStockpile uses, without adding anything - 0 if
        /// blocked, not a stockpile, or the item is unknown. Lets
        /// foraging work decide how much to dig before it ever walks to
        /// the stockpile.
        /// </summary>
        public int GetRemainingRoomForItem(EntityId zoneId, string itemContentId)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone) || zone.ZoneTypeId != StockpileTypeId)
            {
                return 0;
            }

            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return 0;
            }

            bool blocked = zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry blockEntry) && blockEntry.Blocked;
            if (blocked)
            {
                return 0;
            }

            int capacityPerPile = CapacityPerPile(item.PileShape, item.Size);
            int currentAmount = AmountStoredOf(zone, itemContentId);
            int defaultLimit = DefaultLimitFor(zone, itemContentId);
            int limit = zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry limitEntry) ? limitEntry.KeepOnHandLimitOverride ?? defaultLimit : defaultLimit;

            int limitRoom = Math.Max(0, limit - currentAmount);

            int spotRoom = 0;
            foreach (StockSpot spot in zone.Spots)
            {
                foreach (StockPile pile in spot.Piles)
                {
                    if (pile.ItemContentId == itemContentId)
                    {
                        spotRoom += capacityPerPile - pile.AmountStored;
                    }
                }

                spotRoom += StockSpotQuarters.MaxAdditionalPiles(spot, item.PileShape) * capacityPerPile;
            }

            return Math.Max(0, Math.Min(spotRoom, limitRoom));
        }

        /// <summary>The spot a delivery of this item should go to next: the spot already holding a pile of it with room, else the spot TryFindSpotForNewPile would start a new pile in. Null if no spot can take it.</summary>
        public bool TryFindDeliverySpot(EntityId zoneId, string itemContentId, out StockSpot spot)
        {
            spot = null;
            if (!_zones.TryGet(zoneId, out ZoneState zone) || zone.ZoneTypeId != StockpileTypeId)
            {
                return false;
            }

            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return false;
            }

            int capacityPerPile = CapacityPerPile(item.PileShape, item.Size);
            (StockSpot existingSpot, StockPile existingPile) = FindPileWithRoom(zone, itemContentId, capacityPerPile);
            if (existingPile != null)
            {
                spot = existingSpot;
                return true;
            }

            if (TryFindSpotForNewPile(zone, itemContentId, item.PileShape, out StockSpot newSpot, out _))
            {
                spot = newSpot;
                return true;
            }

            return false;
        }

        /// <summary>The spot RemoveItemFromStockpile would take from next (the spot holding this item's least-full pile) - lets a hungry person walk toward the spot they'll actually eat from.</summary>
        public bool TryFindTakeSpot(EntityId zoneId, string itemContentId, out StockSpot spot)
        {
            spot = null;
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return false;
            }

            (StockSpot foundSpot, StockPile pile) = FindLeastFullPile(zone, itemContentId);
            if (pile == null)
            {
                return false;
            }

            spot = foundSpot;
            return true;
        }

        /// <summary>A cell of this zone next to the given spot (not one of the cells the spot itself covers) for a person to stand on while dropping off or picking up - the nearest such cell to the spot's center, or the spot's own cell if somehow none exists.</summary>
        public bool TryGetApproachCellForSpot(EntityId zoneId, StockSpot spot, out CellCoord approachCell)
        {
            approachCell = default;
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return false;
            }

            double spotSize = _settings.Get(SpotSizeMetersSettingId);
            HashSet<CellCoord> spotCells = CellRasterizer.RasterizeRectangleToCells(spot.CenterX, spot.CenterZ, spotSize, spotSize, spot.RotationDegrees, _mapModule.CellSizeMeters);
            CellCoord spotCenterCell = _mapModule.WorldToCell(spot.CenterX, spot.CenterZ);

            bool found = false;
            CellCoord best = default;
            double bestDistance = double.PositiveInfinity;

            foreach (CellCoord cell in zone.Cells.OrderBy(c => c.X).ThenBy(c => c.Z))
            {
                if (spotCells.Contains(cell))
                {
                    continue;
                }

                double dx = cell.X - spotCenterCell.X;
                double dz = cell.Z - spotCenterCell.Z;
                double distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                    found = true;
                }
            }

            approachCell = found ? best : spotCenterCell;
            return true;
        }

        /// <summary>The first pile of this item (in spot order) with room for more, anywhere in the stockpile - null if none.</summary>
        private static (StockSpot Spot, StockPile Pile) FindPileWithRoom(ZoneState zone, string itemContentId, int capacityPerPile)
        {
            foreach (StockSpot spot in zone.Spots)
            {
                foreach (StockPile pile in spot.Piles)
                {
                    if (pile.ItemContentId == itemContentId && pile.AmountStored < capacityPerPile)
                    {
                        return (spot, pile);
                    }
                }
            }

            return (null, null);
        }

        /// <summary>The least-full pile of this item anywhere in the stockpile (amount &gt; 0) - null if it has none.</summary>
        private static (StockSpot Spot, StockPile Pile) FindLeastFullPile(ZoneState zone, string itemContentId)
        {
            StockSpot bestSpot = null;
            StockPile bestPile = null;

            foreach (StockSpot spot in zone.Spots)
            {
                foreach (StockPile pile in spot.Piles)
                {
                    if (pile.ItemContentId == itemContentId && pile.AmountStored > 0 && (bestPile == null || pile.AmountStored < bestPile.AmountStored))
                    {
                        bestSpot = spot;
                        bestPile = pile;
                    }
                }
            }

            return (bestSpot, bestPile);
        }

        /// <summary>
        /// R22: where a new pile of this item and shape should start, in
        /// order: a spot that already holds this item with free quarters
        /// for the shape (first found); else the nearest spot already
        /// partly used by other items with free quarters for the shape;
        /// else the empty spot nearest this item's other piles (any empty
        /// spot, if it has none). False if nothing fits anywhere.
        /// </summary>
        private static bool TryFindSpotForNewPile(ZoneState zone, string itemContentId, PileShape shape, out StockSpot spot, out List<int> quarters)
        {
            foreach (StockSpot candidate in zone.Spots)
            {
                if (!candidate.Piles.Any(p => p.ItemContentId == itemContentId))
                {
                    continue;
                }

                List<int> free = StockSpotQuarters.FindFreeQuarters(candidate, shape);
                if (free != null)
                {
                    spot = candidate;
                    quarters = free;
                    return true;
                }
            }

            List<StockSpot> existingItemSpots = zone.Spots.Where(s => s.Piles.Any(p => p.ItemContentId == itemContentId)).ToList();

            StockSpot bestPartlyUsed = null;
            List<int> bestPartlyUsedQuarters = null;
            double bestPartlyUsedDistance = double.PositiveInfinity;

            foreach (StockSpot candidate in zone.Spots)
            {
                if (candidate.Piles.Count == 0)
                {
                    continue;
                }

                List<int> free = StockSpotQuarters.FindFreeQuarters(candidate, shape);
                if (free == null)
                {
                    continue;
                }

                double distance = existingItemSpots.Count == 0 ? 0 : existingItemSpots.Min(s => DistanceSquared(s, candidate));
                if (distance < bestPartlyUsedDistance)
                {
                    bestPartlyUsedDistance = distance;
                    bestPartlyUsed = candidate;
                    bestPartlyUsedQuarters = free;
                }
            }

            if (bestPartlyUsed != null)
            {
                spot = bestPartlyUsed;
                quarters = bestPartlyUsedQuarters;
                return true;
            }

            StockSpot bestEmpty = null;
            double bestEmptyDistance = double.PositiveInfinity;

            foreach (StockSpot candidate in zone.Spots)
            {
                if (candidate.Piles.Count != 0)
                {
                    continue;
                }

                double distance = existingItemSpots.Count == 0 ? 0 : existingItemSpots.Min(s => DistanceSquared(s, candidate));
                if (distance < bestEmptyDistance)
                {
                    bestEmptyDistance = distance;
                    bestEmpty = candidate;
                }
            }

            if (bestEmpty != null)
            {
                spot = bestEmpty;
                quarters = StockSpotQuarters.FindFreeQuarters(bestEmpty, shape);
                return true;
            }

            spot = null;
            quarters = null;
            return false;
        }

        /// <summary>Delivers as much of amount as piles/quarters allow, ignoring keep-on-hand limits and blocks - used only by save migrations, which are restoring old state rather than making a new delivery under today's rules. Returns how much was actually placed.</summary>
        private int DeliverIgnoringLimitsAndBlocks(EntityId zoneId, ZoneState zone, string itemContentId, ItemContent item, int amount)
        {
            int capacityPerPile = CapacityPerPile(item.PileShape, item.Size);
            int remaining = amount;

            while (remaining > 0)
            {
                (StockSpot existingSpot, StockPile existingPile) = FindPileWithRoom(zone, itemContentId, capacityPerPile);
                if (existingPile != null)
                {
                    int addNow = Math.Min(remaining, capacityPerPile - existingPile.AmountStored);
                    existingPile.AmountStored += addNow;
                    remaining -= addNow;
                    continue;
                }

                if (!TryFindSpotForNewPile(zone, itemContentId, item.PileShape, out StockSpot newSpot, out List<int> quarters))
                {
                    break;
                }

                int addNowNew = Math.Min(remaining, capacityPerPile);
                var newPile = new StockPile { ItemContentId = itemContentId, StackModelIndex = PickStackModelIndex(zoneId, item), AmountStored = addNowNew };
                newPile.QuarterIndices.AddRange(quarters);
                newSpot.Piles.Add(newPile);

                remaining -= addNowNew;
            }

            return amount - remaining;
        }

        private static double DistanceSquared(StockSpot a, StockSpot b)
        {
            double dx = a.CenterX - b.CenterX;
            double dz = a.CenterZ - b.CenterZ;
            return dx * dx + dz * dz;
        }

        /// <summary>Picked at random (from the world seed, via a stream named for this zone) whenever a pile starts - kept for its lifetime.</summary>
        private int PickStackModelIndex(EntityId zoneId, ItemContent item)
        {
            if (item.StackModelCount <= 0)
            {
                return 0;
            }

            SimRandom random = _randomService.GetStream($"zones.stack_models.{zoneId}");
            return random.NextInt(0, item.StackModelCount);
        }

        private int AmountStoredOf(ZoneState zone, string itemContentId)
        {
            int total = 0;
            foreach (StockSpot spot in zone.Spots)
            {
                foreach (StockPile pile in spot.Piles)
                {
                    if (pile.ItemContentId == itemContentId)
                    {
                        total += pile.AmountStored;
                    }
                }
            }

            return total;
        }

        /// <summary>The most this stockpile could hold of this item alone: how many piles of its shape the stockpile's total quarters could fit, times one pile's capacity.</summary>
        private int DefaultLimitFor(ZoneState zone, string itemContentId)
        {
            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return 0;
            }

            int capacityPerPile = CapacityPerPile(item.PileShape, item.Size);
            int totalQuarters = zone.Spots.Count * StockSpotQuarters.Count;
            int maxPiles = totalQuarters / item.PileShape.QuarterCount();

            return maxPiles * capacityPerPile;
        }

        private static StockpileItemEntry GetOrCreateEntry(ZoneState zone, string itemContentId)
        {
            if (!zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry entry))
            {
                entry = new StockpileItemEntry();
                zone.StockpileItems[itemContentId] = entry;
            }

            return entry;
        }

        #endregion

        #region Occupancy

        /// <summary>
        /// R21: the cells occupied right now - covered by a stockpile
        /// spot that holds anything. Occupied cells can never be
        /// removed by erasing, Erase all, Delete or a type change. The
        /// one place this is computed, so a future occupant (a house,
        /// say) only needs to extend this method, not every caller that
        /// already checks it.
        /// </summary>
        public HashSet<CellCoord> GetOccupiedCells(EntityId zoneId)
        {
            return _zones.TryGet(zoneId, out ZoneState zone) ? OccupiedCellsOf(zone) : new HashSet<CellCoord>();
        }

        /// <summary>Whether this zone holds anything anywhere right now - a stockpile with any occupied spot. A Living Area (or anything else with no spots) is never occupied.</summary>
        public bool IsZoneOccupied(EntityId zoneId)
        {
            return _zones.TryGet(zoneId, out ZoneState zone) && zone.Spots.Any(s => s.Piles.Count > 0);
        }

        private HashSet<CellCoord> OccupiedCellsOf(ZoneState zone)
        {
            var cells = new HashSet<CellCoord>();
            if (zone.Spots.Count == 0)
            {
                return cells;
            }

            double spotSize = _settings.Get(SpotSizeMetersSettingId);
            double cellSize = _mapModule.CellSizeMeters;

            foreach (StockSpot spot in zone.Spots)
            {
                if (spot.Piles.Count == 0)
                {
                    continue;
                }

                cells.UnionWith(CellRasterizer.RasterizeRectangleToCells(spot.CenterX, spot.CenterZ, spotSize, spotSize, spot.RotationDegrees, cellSize));
            }

            return cells;
        }

        #endregion

        #region Modify and delete

        /// <summary>
        /// R21: applies a Modify session's edits (the "Save changes"
        /// Yes). Occupied cells are kept no matter what newCells says -
        /// the UI must never let them be erased, but this is the one
        /// place that actually guarantees it, so a stockpile holding
        /// anything can never lose that type change either. Otherwise,
        /// only cells CanPaintCell still allows (excluding this zone
        /// itself) are kept, same as creation. A type change clears any
        /// existing spots (and item limits/blocks, meaningless for a
        /// different type) and, if the new type is a stockpile, lays
        /// out a fresh layout. Staying a stockpile reconciles spots
        /// instead - see ReconcileStockpileSpots.
        /// </summary>
        public void ApplyZoneEdit(EntityId zoneId, string name, string colorHex, string zoneTypeId, IEnumerable<CellCoord> newCells, string layoutId)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return;
            }

            HashSet<CellCoord> occupiedCells = OccupiedCellsOf(zone);
            if (occupiedCells.Count > 0)
            {
                zoneTypeId = zone.ZoneTypeId;
            }

            zone.Name = name;
            zone.ColorHex = colorHex;

            var finalCells = new HashSet<CellCoord>();
            foreach (CellCoord cell in newCells)
            {
                if (CanPaintCell(cell, zoneId))
                {
                    finalCells.Add(cell);
                }
            }

            finalCells.UnionWith(occupiedCells);

            bool typeChanged = zone.ZoneTypeId != zoneTypeId;

            zone.Cells.Clear();
            foreach (CellCoord cell in finalCells)
            {
                zone.Cells.Add(cell);
            }

            RecomputeCentroid(zone);

            if (typeChanged)
            {
                zone.ZoneTypeId = zoneTypeId;
                zone.Spots.Clear();
                zone.StockpileItems.Clear();

                if (zoneTypeId == StockpileTypeId)
                {
                    zone.LayoutId = layoutId == LayoutUniformId ? LayoutUniformId : LayoutNaturalId;
                    GenerateSpots(zoneId, zone);
                }
                else
                {
                    zone.LayoutId = null;
                }

                return;
            }

            if (zoneTypeId == StockpileTypeId)
            {
                ReconcileStockpileSpots(zoneId, zone, layoutId);
            }
        }

        /// <summary>
        /// R21: keeps every spot that still fits fully inside the
        /// (possibly changed) painted cells - an occupied one always
        /// does, since ApplyZoneEdit never lets its cells go missing -
        /// then fills whatever's left with a freshly generated layout,
        /// dropping any new candidate that would overlap a kept spot.
        /// </summary>
        private void ReconcileStockpileSpots(EntityId zoneId, ZoneState zone, string layoutId)
        {
            double spotSize = _settings.Get(SpotSizeMetersSettingId);
            double cellSize = _mapModule.CellSizeMeters;

            var keptSpots = new List<StockSpot>();
            var coveredCells = new HashSet<CellCoord>();

            foreach (StockSpot spot in zone.Spots)
            {
                HashSet<CellCoord> covered = CellRasterizer.RasterizeRectangleToCells(spot.CenterX, spot.CenterZ, spotSize, spotSize, spot.RotationDegrees, cellSize);
                if (covered.Count > 0 && covered.All(zone.Cells.Contains))
                {
                    keptSpots.Add(spot);
                    coveredCells.UnionWith(covered);
                }
            }

            zone.LayoutId = layoutId == LayoutUniformId ? LayoutUniformId : LayoutNaturalId;

            SimRandom random = _randomService.GetStream($"zones.layout.{zoneId}");
            List<StockSpot> candidates = StockSpotLayoutGenerator.Generate(
                zone.LayoutId,
                zone.Cells,
                cellSize,
                spotSize,
                (int)_settings.Get(NaturalPatchMinSpotsPerSideSettingId),
                (int)_settings.Get(NaturalPatchMaxSpotsPerSideSettingId),
                _settings.Get(NaturalPatchMinAngleDegreesSettingId),
                _settings.Get(NaturalPatchMaxAngleDegreesSettingId),
                (int)_settings.Get(NaturalMaxPlacementAttemptsSettingId),
                random);

            zone.Spots.Clear();
            zone.Spots.AddRange(keptSpots);

            foreach (StockSpot candidate in candidates)
            {
                HashSet<CellCoord> covered = CellRasterizer.RasterizeRectangleToCells(candidate.CenterX, candidate.CenterZ, spotSize, spotSize, candidate.RotationDegrees, cellSize);
                if (covered.Overlaps(coveredCells))
                {
                    continue;
                }

                zone.Spots.Add(candidate);
                coveredCells.UnionWith(covered);
            }
        }

        /// <summary>
        /// R21: removes every unoccupied cell and spot. If nothing was
        /// occupied, the zone is removed entirely. If anything was
        /// occupied, the zone stays with just its occupied spots and
        /// cells, ready to be deleted again once they're empty.
        /// </summary>
        public ZoneDeleteResult DeleteZone(EntityId zoneId)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return new ZoneDeleteResult(true, 0);
            }

            List<StockSpot> occupiedSpots = zone.Spots.Where(s => s.Piles.Count > 0).ToList();

            if (occupiedSpots.Count == 0)
            {
                _entities.Destroy(zoneId);
                return new ZoneDeleteResult(true, 0);
            }

            HashSet<CellCoord> occupiedCells = OccupiedCellsOf(zone);
            zone.Cells.IntersectWith(occupiedCells);
            zone.Spots.Clear();
            zone.Spots.AddRange(occupiedSpots);
            RecomputeCentroid(zone);

            return new ZoneDeleteResult(false, occupiedSpots.Count);
        }

        #endregion

        #region Save/load

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_zones.Count);
            foreach (KeyValuePair<EntityId, ZoneState> row in _zones.Rows())
            {
                writer.WriteEntityId(row.Key);
                ZoneState zone = row.Value;

                writer.WriteString(zone.ZoneTypeId);
                writer.WriteString(zone.Name ?? string.Empty);
                writer.WriteString(zone.ColorHex ?? string.Empty);
                writer.WriteDouble(zone.CenterX);
                writer.WriteDouble(zone.CenterZ);

                writer.WriteInt(zone.Cells.Count);
                foreach (CellCoord cell in zone.Cells.OrderBy(c => c.X).ThenBy(c => c.Z))
                {
                    writer.WriteInt(cell.X);
                    writer.WriteInt(cell.Z);
                }

                writer.WriteString(zone.LayoutId ?? string.Empty);
                WriteSpots(writer, zone);
                WriteStockpileItems(writer, zone);
            }
        }

        private static void WriteSpots(SaveWriter writer, ZoneState zone)
        {
            writer.WriteInt(zone.Spots.Count);
            foreach (StockSpot spot in zone.Spots)
            {
                writer.WriteDouble(spot.CenterX);
                writer.WriteDouble(spot.CenterZ);
                writer.WriteDouble(spot.RotationDegrees);

                writer.WriteInt(spot.Piles.Count);
                foreach (StockPile pile in spot.Piles)
                {
                    writer.WriteString(pile.ItemContentId ?? string.Empty);
                    writer.WriteInt(pile.AmountStored);
                    writer.WriteInt(pile.StackModelIndex);

                    writer.WriteInt(pile.QuarterIndices.Count);
                    foreach (int quarter in pile.QuarterIndices)
                    {
                        writer.WriteInt(quarter);
                    }
                }
            }
        }

        private static void WriteStockpileItems(SaveWriter writer, ZoneState zone)
        {
            writer.WriteInt(zone.StockpileItems.Count);
            foreach (KeyValuePair<string, StockpileItemEntry> kvp in zone.StockpileItems)
            {
                writer.WriteString(kvp.Key);
                writer.WriteBool(kvp.Value.KeepOnHandLimitOverride.HasValue);
                writer.WriteInt(kvp.Value.KeepOnHandLimitOverride ?? 0);
                writer.WriteBool(kvp.Value.Blocked);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                ZoneState zone = savedVersion < 2 ? ReadOldRectangleZone(reader) : ReadCellsZone(reader);

                if (savedVersion < 3)
                {
                    ReadAndMigrateOldStockpileItems(reader, id, zone);
                }
                else
                {
                    zone.LayoutId = reader.ReadString();

                    if (savedVersion < 4)
                    {
                        List<(int SpotIndex, string ItemId, int Amount, int StackModelIndex)> oldStacks = ReadSpotsOldSingleStackFormat(reader, zone);
                        ReadStockpileItems(reader, zone);
                        MigrateOldSpotStacksToPiles(id, zone, oldStacks);
                    }
                    else
                    {
                        ReadSpots(reader, zone);
                        ReadStockpileItems(reader, zone);
                    }
                }

                _zones.Add(id, zone);
            }
        }

        private ZoneState ReadCellsZone(SaveReader reader)
        {
            var zone = new ZoneState
            {
                ZoneTypeId = reader.ReadString(),
                Name = reader.ReadString(),
                ColorHex = reader.ReadString(),
                CenterX = reader.ReadDouble(),
                CenterZ = reader.ReadDouble()
            };

            int cellCount = reader.ReadInt();
            for (int j = 0; j < cellCount; j++)
            {
                zone.Cells.Add(new CellCoord(reader.ReadInt(), reader.ReadInt()));
            }

            return zone;
        }

        private static void ReadSpots(SaveReader reader, ZoneState zone)
        {
            int spotCount = reader.ReadInt();
            for (int j = 0; j < spotCount; j++)
            {
                double centerX = reader.ReadDouble();
                double centerZ = reader.ReadDouble();
                double rotation = reader.ReadDouble();

                var spot = new StockSpot { CenterX = centerX, CenterZ = centerZ, RotationDegrees = rotation };

                int pileCount = reader.ReadInt();
                for (int p = 0; p < pileCount; p++)
                {
                    string itemId = reader.ReadString();
                    int amount = reader.ReadInt();
                    int stackModelIndex = reader.ReadInt();

                    var pile = new StockPile
                    {
                        ItemContentId = string.IsNullOrEmpty(itemId) ? null : itemId,
                        AmountStored = amount,
                        StackModelIndex = stackModelIndex
                    };

                    int quarterCount = reader.ReadInt();
                    for (int q = 0; q < quarterCount; q++)
                    {
                        pile.QuarterIndices.Add(reader.ReadInt());
                    }

                    spot.Piles.Add(pile);
                }

                zone.Spots.Add(spot);
            }
        }

        /// <summary>R20/R21 format (SectionVersion 3): one stack per spot, no quarters/piles. Builds the spots with their positions and returns each non-empty spot's old (single) stack for MigrateOldSpotStacksToPiles to convert, after the stockpile items dict (which follows this, in the stream) is also read.</summary>
        private static List<(int SpotIndex, string ItemId, int Amount, int StackModelIndex)> ReadSpotsOldSingleStackFormat(SaveReader reader, ZoneState zone)
        {
            int spotCount = reader.ReadInt();
            var oldStacks = new List<(int SpotIndex, string ItemId, int Amount, int StackModelIndex)>();

            for (int j = 0; j < spotCount; j++)
            {
                double centerX = reader.ReadDouble();
                double centerZ = reader.ReadDouble();
                double rotation = reader.ReadDouble();
                string itemId = reader.ReadString();
                int amount = reader.ReadInt();
                int stackModelIndex = reader.ReadInt();

                zone.Spots.Add(new StockSpot { CenterX = centerX, CenterZ = centerZ, RotationDegrees = rotation });

                if (!string.IsNullOrEmpty(itemId) && amount > 0)
                {
                    oldStacks.Add((zone.Spots.Count - 1, itemId, amount, stackModelIndex));
                }
            }

            return oldStacks;
        }

        private static void ReadStockpileItems(SaveReader reader, ZoneState zone)
        {
            int itemCount = reader.ReadInt();
            for (int j = 0; j < itemCount; j++)
            {
                string itemId = reader.ReadString();
                bool hasOverride = reader.ReadBool();
                int overrideValue = reader.ReadInt();
                bool blocked = reader.ReadBool();

                zone.StockpileItems[itemId] = new StockpileItemEntry
                {
                    KeepOnHandLimitOverride = hasOverride ? overrideValue : (int?)null,
                    Blocked = blocked
                };
            }
        }

        /// <summary>
        /// R22: pre-R22 saves (SectionVersion &lt; 4) stored one single
        /// stack per spot. Each old stack is first split into that
        /// item's own piles inside the same spot it was already in;
        /// anything that doesn't fit there moves into free room elsewhere
        /// in the stockpile, by the normal delivery rules (ignoring
        /// keep-on-hand limits and blocks, since this is restoring old
        /// state, not a new delivery under today's rules); anything that
        /// still doesn't fit is dropped - a one-time migration cost, not
        /// an ongoing rule.
        /// </summary>
        private void MigrateOldSpotStacksToPiles(EntityId zoneId, ZoneState zone, List<(int SpotIndex, string ItemId, int Amount, int StackModelIndex)> oldStacks)
        {
            foreach ((int spotIndex, string itemId, int amount, int stackModelIndex) in oldStacks)
            {
                if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
                {
                    continue;
                }

                StockSpot spot = zone.Spots[spotIndex];
                int capacityPerPile = CapacityPerPile(item.PileShape, item.Size);
                int remaining = amount;

                while (remaining > 0)
                {
                    List<int> quarters = StockSpotQuarters.FindFreeQuarters(spot, item.PileShape);
                    if (quarters == null)
                    {
                        break;
                    }

                    int addNow = Math.Min(remaining, capacityPerPile);
                    var pile = new StockPile { ItemContentId = itemId, StackModelIndex = stackModelIndex, AmountStored = addNow };
                    pile.QuarterIndices.AddRange(quarters);
                    spot.Piles.Add(pile);

                    remaining -= addNow;
                }

                if (remaining > 0)
                {
                    DeliverIgnoringLimitsAndBlocks(zoneId, zone, itemId, item, remaining);
                }
            }
        }

        /// <summary>
        /// R20: pre-R20 saves (SectionVersion &lt; 3) stored each item's
        /// amount directly, with no spots or layout at all. Reads that
        /// old format, then - for a stockpile - generates a fresh
        /// (Natural) layout for its painted cells and lays each item's
        /// old amount into that layout's piles, by the normal delivery
        /// rules, ignoring keep-on-hand limits and blocks since this is
        /// restoring old state, not a new delivery under today's rules.
        /// If the new layout genuinely can't hold everything it used to
        /// (a Natural layout rarely packs as tightly as the old abstract
        /// slot count did), the leftover is dropped - a one-time
        /// migration cost, not an ongoing rule.
        /// </summary>
        private void ReadAndMigrateOldStockpileItems(SaveReader reader, EntityId id, ZoneState zone)
        {
            int itemCount = reader.ReadInt();
            var oldAmounts = new List<(string ItemId, int Amount)>();

            for (int j = 0; j < itemCount; j++)
            {
                string itemId = reader.ReadString();
                int amountStored = reader.ReadInt();
                bool hasOverride = reader.ReadBool();
                int overrideValue = reader.ReadInt();
                bool blocked = reader.ReadBool();

                zone.StockpileItems[itemId] = new StockpileItemEntry
                {
                    KeepOnHandLimitOverride = hasOverride ? overrideValue : (int?)null,
                    Blocked = blocked
                };

                if (amountStored > 0)
                {
                    oldAmounts.Add((itemId, amountStored));
                }
            }

            if (zone.ZoneTypeId != StockpileTypeId)
            {
                return;
            }

            zone.LayoutId = LayoutNaturalId;
            GenerateSpots(id, zone);

            foreach ((string itemId, int amount) in oldAmounts.OrderBy(e => e.ItemId, StringComparer.Ordinal))
            {
                LayInOldAmount(id, zone, itemId, amount);
            }
        }

        private void LayInOldAmount(EntityId zoneId, ZoneState zone, string itemContentId, int amount)
        {
            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return;
            }

            DeliverIgnoringLimitsAndBlocks(zoneId, zone, itemContentId, item, amount);
        }

        /// <summary>
        /// R18b: pre-R18b saves stored a rotated rectangle instead of
        /// cells - rasterized here into the cells it covers, with a
        /// default name and its type's own tint, since neither existed
        /// yet. The rotated-rectangle math itself is otherwise gone
        /// (removed with the old drag tool); this is the one place it's
        /// still needed, for this one-time conversion.
        /// </summary>
        private ZoneState ReadOldRectangleZone(SaveReader reader)
        {
            string zoneTypeId = reader.ReadString();
            double centerX = reader.ReadDouble();
            double centerZ = reader.ReadDouble();
            double sideALength = reader.ReadDouble();
            double sideBLength = reader.ReadDouble();
            double rotationDegrees = reader.ReadDouble();

            var zone = new ZoneState
            {
                ZoneTypeId = zoneTypeId,
                Name = DefaultNameForType(zoneTypeId),
                ColorHex = DefaultColorHexForType(zoneTypeId)
            };

            foreach (CellCoord cell in CellRasterizer.RasterizeRectangleToCells(centerX, centerZ, sideALength, sideBLength, rotationDegrees, _mapModule.CellSizeMeters))
            {
                zone.Cells.Add(cell);
            }

            RecomputeCentroid(zone);
            return zone;
        }

        private string DefaultNameForType(string zoneTypeId)
        {
            if (_content.TryGet(BuiltInContentTypes.ZoneTypeTypeName, zoneTypeId, out ContentEntry entry) && entry.Definition is ZoneTypeDefinition zoneType)
            {
                return zoneType.DisplayName + " Zone";
            }

            return "Zone";
        }

        private string DefaultColorHexForType(string zoneTypeId)
        {
            if (_content.TryGet(BuiltInContentTypes.ZoneTypeTypeName, zoneTypeId, out ContentEntry entry) && entry.Definition is ZoneTypeDefinition zoneType)
            {
                return zoneType.ColorHex;
            }

            return "#808080";
        }

        #endregion
    }
}
