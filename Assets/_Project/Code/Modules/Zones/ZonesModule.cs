using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Ponds;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R18b: placed zones - sets of cells (which may form several
    /// separate pieces), each with its own name and color. Paint can
    /// never claim a cell already in another zone, a crown building's
    /// footprint, or pond water, but may cover trees, stones and grub
    /// mounds. Stockpile zones additionally track, per item type, how
    /// much is stored, a keep-on-hand limit and whether it's blocked.
    /// Depends on Map (cell size, cell/world conversions), Buildings
    /// (the crown-building check) and Ponds (the pond-water check).
    /// </summary>
    public sealed class ZonesModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.zones";
        public const string StockpileTypeId = "zone.stockpile";
        public const string LivingAreaTypeId = "zone.living_area";

        /// <summary>R18b: how many painted cells one stockpile slot holds, placeholder.</summary>
        public const string CellsPerSlotSettingId = "setting.zones.cells_per_slot";

        /// <summary>Placeholder: how many size-1 capacity units one slot holds. Unrelated to cells; not touched by R18b.</summary>
        public const int CapacityUnitsPerSlot = 100;

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
        public int SectionVersion => 2;
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
            _context = context;
            _zones = new EntityTable<ZoneState>(_entities);
        }

        public void Shutdown()
        {
        }

        public static int CapacityPerSlot(int itemSize) => Math.Max(1, CapacityUnitsPerSlot / Math.Max(1, itemSize));

        public static int SlotsNeededFor(int amount, int itemSize)
        {
            if (amount <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(amount / (double)CapacityPerSlot(itemSize));
        }

        /// <summary>One slot per setting.zones.cells_per_slot painted cells, rounded down.</summary>
        public int SlotCount(int cellCount)
        {
            int cellsPerSlot = Math.Max(1, (int)_settings.Get(CellsPerSlotSettingId));
            return Math.Max(0, cellCount / cellsPerSlot);
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
        /// so this stays honest even if called with a stale set.
        /// </summary>
        public EntityId CreateZone(string zoneTypeId, string name, string colorHex, IEnumerable<CellCoord> cells)
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
            _zones.Add(id, state);
            _context.Events.Post(ZonePlacedEventId, _context.CurrentTick, new EventData().SetString("contentId", zoneTypeId));
            return id;
        }

        public void RemoveZone(EntityId id)
        {
            if (_zones.Has(id))
            {
                _entities.Destroy(id);
            }
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

        /// <summary>The live view of one item type at one stockpile - amount stored, its keep-on-hand limit (defaulted if never overridden), and whether it's blocked.</summary>
        public (int Amount, int Limit, bool Blocked) GetStockpileItemInfo(EntityId zoneId, string itemContentId)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return (0, 0, false);
            }

            int defaultLimit = DefaultLimitFor(zone, itemContentId);

            if (zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry entry))
            {
                return (entry.AmountStored, entry.KeepOnHandLimitOverride ?? defaultLimit, entry.Blocked);
            }

            return (0, defaultLimit, false);
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

        /// <summary>R13: removes up to amount of this item (eating a grub from a stockpile, say); returns how much was actually removed, never more than what's stored.</summary>
        public int RemoveItemFromStockpile(EntityId zoneId, string itemContentId, int amount)
        {
            if (amount <= 0 || !_zones.TryGet(zoneId, out ZoneState zone) || !zone.StockpileItems.TryGetValue(itemContentId, out StockpileItemEntry entry))
            {
                return 0;
            }

            int actual = Math.Min(amount, entry.AmountStored);
            entry.AmountStored -= actual;
            return actual;
        }

        /// <summary>
        /// Adds as much of amountRequested as slots, size, blocks and the
        /// keep-on-hand limit allow; Added may be less than requested.
        /// Reason is null when everything fit.
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

            (int currentAmount, int limit, bool blocked) = GetStockpileItemInfo(zoneId, itemContentId);
            if (blocked)
            {
                return (0, amountRequested, "Blocked here.");
            }

            (int maxAddBySlots, int maxAddByLimit) = ComputeMaxAdd(zone, item, itemContentId, currentAmount, limit);

            int actualAdd = Math.Max(0, Math.Min(amountRequested, Math.Min(maxAddBySlots, maxAddByLimit)));
            int didNotFit = amountRequested - actualAdd;

            if (actualAdd > 0)
            {
                GetOrCreateEntry(zone, itemContentId).AmountStored += actualAdd;
                _context.Events.Post(ItemStoredEventId, _context.CurrentTick, new EventData().SetString("contentId", itemContentId));
            }

            string reason = didNotFit <= 0 ? null : maxAddByLimit < maxAddBySlots ? "Hit the keep-on-hand limit." : "Not enough room left.";
            return (actualAdd, didNotFit, reason);
        }

        /// <summary>
        /// R13: how many more of this item this stockpile could currently
        /// take, the same capacity math TryAddItemToStockpile uses, without
        /// adding anything - 0 if blocked, not a stockpile, or the item is
        /// unknown. Lets foraging work decide how much to dig before it
        /// ever walks to the stockpile.
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

            (int currentAmount, int limit, bool blocked) = GetStockpileItemInfo(zoneId, itemContentId);
            if (blocked)
            {
                return 0;
            }

            (int maxAddBySlots, int maxAddByLimit) = ComputeMaxAdd(zone, item, itemContentId, currentAmount, limit);
            return Math.Max(0, Math.Min(maxAddBySlots, maxAddByLimit));
        }

        private (int MaxAddBySlots, int MaxAddByLimit) ComputeMaxAdd(ZoneState zone, ItemContent item, string itemContentId, int currentAmount, int limit)
        {
            int totalSlots = SlotCount(zone.Cells.Count);
            int capacityPerSlot = CapacityPerSlot(item.Size);

            int slotsUsedByOthers = zone.StockpileItems
                .Where(kvp => kvp.Key != itemContentId)
                .Sum(kvp => SlotsForOther(kvp));

            int maxNewTotalBySlots = Math.Max(0, totalSlots - slotsUsedByOthers) * capacityPerSlot;
            int maxAddBySlots = Math.Max(0, maxNewTotalBySlots - currentAmount);
            int maxAddByLimit = Math.Max(0, limit - currentAmount);

            return (maxAddBySlots, maxAddByLimit);
        }

        private int SlotsForOther(KeyValuePair<string, StockpileItemEntry> kvp)
        {
            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, kvp.Key, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return 0;
            }

            return SlotsNeededFor(kvp.Value.AmountStored, item.Size);
        }

        private int DefaultLimitFor(ZoneState zone, string itemContentId)
        {
            if (!_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) || !(entry.Definition is ItemContent item))
            {
                return 0;
            }

            return SlotCount(zone.Cells.Count) * CapacityPerSlot(item.Size);
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

                WriteStockpileItems(writer, zone);
            }
        }

        private static void WriteStockpileItems(SaveWriter writer, ZoneState zone)
        {
            writer.WriteInt(zone.StockpileItems.Count);
            foreach (KeyValuePair<string, StockpileItemEntry> kvp in zone.StockpileItems)
            {
                writer.WriteString(kvp.Key);
                writer.WriteInt(kvp.Value.AmountStored);
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
                ReadStockpileItems(reader, zone);
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

            foreach (CellCoord cell in RasterizeRectangleToCells(centerX, centerZ, sideALength, sideBLength, rotationDegrees, _mapModule.CellSizeMeters))
            {
                zone.Cells.Add(cell);
            }

            RecomputeCentroid(zone);
            return zone;
        }

        private static void ReadStockpileItems(SaveReader reader, ZoneState zone)
        {
            int itemCount = reader.ReadInt();
            for (int j = 0; j < itemCount; j++)
            {
                string itemId = reader.ReadString();
                int amountStored = reader.ReadInt();
                bool hasOverride = reader.ReadBool();
                int overrideValue = reader.ReadInt();
                bool blocked = reader.ReadBool();

                zone.StockpileItems[itemId] = new StockpileItemEntry
                {
                    AmountStored = amountStored,
                    KeepOnHandLimitOverride = hasOverride ? overrideValue : (int?)null,
                    Blocked = blocked
                };
            }
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

        /// <summary>Save-migration-only: the same rotated-rectangle containment test the old OrientedRect used, inlined here since OrientedRect itself is gone.</summary>
        private static HashSet<CellCoord> RasterizeRectangleToCells(double centerX, double centerZ, double sideALength, double sideBLength, double rotationDegrees, double cellSizeMeters)
        {
            double rad = rotationDegrees * Math.PI / 180.0;
            double axisAx = Math.Cos(rad);
            double axisAz = Math.Sin(rad);
            double axisBx = -axisAz;
            double axisBz = axisAx;
            double halfA = Math.Abs(sideALength) * 0.5;
            double halfB = Math.Abs(sideBLength) * 0.5;

            double extentX = Math.Abs(axisAx) * halfA + Math.Abs(axisBx) * halfB;
            double extentZ = Math.Abs(axisAz) * halfA + Math.Abs(axisBz) * halfB;

            int minCellX = (int)Math.Floor((centerX - extentX) / cellSizeMeters);
            int maxCellX = (int)Math.Ceiling((centerX + extentX) / cellSizeMeters);
            int minCellZ = (int)Math.Floor((centerZ - extentZ) / cellSizeMeters);
            int maxCellZ = (int)Math.Ceiling((centerZ + extentZ) / cellSizeMeters);

            var cells = new HashSet<CellCoord>();
            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                for (int cz = minCellZ; cz <= maxCellZ; cz++)
                {
                    double worldX = (cx + 0.5) * cellSizeMeters;
                    double worldZ = (cz + 0.5) * cellSizeMeters;
                    double dx = worldX - centerX;
                    double dz = worldZ - centerZ;
                    double localA = dx * axisAx + dz * axisAz;
                    double localB = dx * axisBx + dz * axisBz;

                    if (Math.Abs(localA) <= halfA && Math.Abs(localB) <= halfB)
                    {
                        cells.Add(new CellCoord(cx, cz));
                    }
                }
            }

            return cells;
        }

        #endregion
    }
}
