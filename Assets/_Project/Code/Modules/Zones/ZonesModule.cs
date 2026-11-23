using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R12: placed zones - rectangles at any angle that can't overlap a
    /// building or another zone, but may cover trees, stones and grub
    /// mounds. Stockpile zones additionally track, per item type, how
    /// much is stored, a keep-on-hand limit and whether it's blocked.
    /// Depends on Map (to turn its own cell size into meters for the
    /// building-overlap check) and Buildings (for that check itself).
    /// </summary>
    public sealed class ZonesModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.zones";
        public const string StockpileTypeId = "zone.stockpile";
        public const string LivingAreaTypeId = "zone.living_area";

        /// <summary>Placeholder: one slot per this many square meters.</summary>
        public const double SquareMetersPerSlot = 4.0;

        /// <summary>Placeholder: how many size-1 capacity units one slot holds.</summary>
        public const int CapacityUnitsPerSlot = 100;

        /// <summary>Placeholder: the shortest either side of a zone may be.</summary>
        public const double MinimumSideMeters = 2.0;

        private readonly MapModule _mapModule;
        private readonly BuildingsModule _buildingsModule;
        private EntityRegistry _entities;
        private ContentDatabase _content;
        private EntityTable<ZoneState> _zones;

        public ZonesModule(MapModule mapModule, BuildingsModule buildingsModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
            _buildingsModule = buildingsModule ?? throw new ArgumentNullException(nameof(buildingsModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public int SectionVersion => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId, BuildingsModule.ModuleId };

        public IEnumerable<KeyValuePair<EntityId, ZoneState>> Zones => _zones.Rows();

        public IEnumerable<KeyValuePair<EntityId, ZoneState>> LivingAreas => _zones.Rows().Where(r => r.Value.ZoneTypeId == LivingAreaTypeId);

        public IEnumerable<KeyValuePair<EntityId, ZoneState>> Stockpiles => _zones.Rows().Where(r => r.Value.ZoneTypeId == StockpileTypeId);

        public bool TryGetZone(EntityId id, out ZoneState zone) => _zones.TryGet(id, out zone);

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _content = context.Content;
            _zones = new EntityTable<ZoneState>(_entities);
        }

        public void Shutdown()
        {
        }

        public static int SlotCount(double areaSquareMeters) => Math.Max(0, (int)Math.Floor(areaSquareMeters / SquareMetersPerSlot));

        public static int CapacityPerSlot(int itemSize) => Math.Max(1, CapacityUnitsPerSlot / Math.Max(1, itemSize));

        public static int SlotsNeededFor(int amount, int itemSize)
        {
            if (amount <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(amount / (double)CapacityPerSlot(itemSize));
        }

        /// <summary>Whether a candidate rectangle could be placed: big enough, and overlapping neither a building nor another zone.</summary>
        public bool CanPlace(OrientedRect candidate, out string reason)
        {
            if (Math.Abs(candidate.SideALength) < MinimumSideMeters || Math.Abs(candidate.SideBLength) < MinimumSideMeters)
            {
                reason = $"Too small - the minimum is {MinimumSideMeters:0.#} by {MinimumSideMeters:0.#} m.";
                return false;
            }

            double cellSize = _mapModule.CellSizeMeters;
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildingsModule.Buildings)
            {
                BuildingState building = row.Value;
                var buildingRect = new OrientedRect(building.WorldX, building.WorldZ, building.FootprintWidth * cellSize, building.FootprintDepth * cellSize, 0);
                if (OrientedRect.Overlaps(candidate, buildingRect))
                {
                    reason = "Overlaps a building.";
                    return false;
                }
            }

            foreach (KeyValuePair<EntityId, ZoneState> row in _zones.Rows())
            {
                if (OrientedRect.Overlaps(candidate, row.Value.Rect))
                {
                    reason = "Overlaps another zone.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public EntityId? TryPlaceZone(string zoneTypeId, OrientedRect rect, out string reason)
        {
            if (!CanPlace(rect, out reason))
            {
                return null;
            }

            var state = new ZoneState
            {
                ZoneTypeId = zoneTypeId,
                CenterX = rect.CenterX,
                CenterZ = rect.CenterZ,
                SideALength = rect.SideALength,
                SideBLength = rect.SideBLength,
                RotationDegrees = rect.RotationDegrees
            };

            EntityId id = _entities.Create(EntityKind.Zone);
            _zones.Add(id, state);
            return id;
        }

        public void RemoveZone(EntityId id)
        {
            if (_zones.Has(id))
            {
                _entities.Destroy(id);
            }
        }

        /// <summary>How many people (from the given current set) fall within this zone's rectangle right now. Takes the set as a parameter - this module never reads PeopleModule, so the dependency only ever runs the other way.</summary>
        public int CountPeopleInside(EntityId zoneId, IEnumerable<(double WorldX, double WorldZ)> peoplePositions)
        {
            if (!_zones.TryGet(zoneId, out ZoneState zone))
            {
                return 0;
            }

            OrientedRect rect = zone.Rect;
            return peoplePositions.Count(p => rect.Contains(p.WorldX, p.WorldZ));
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
            int totalSlots = SlotCount(zone.Rect.Area);
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

            return SlotCount(zone.Rect.Area) * CapacityPerSlot(item.Size);
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
                writer.WriteDouble(zone.CenterX);
                writer.WriteDouble(zone.CenterZ);
                writer.WriteDouble(zone.SideALength);
                writer.WriteDouble(zone.SideBLength);
                writer.WriteDouble(zone.RotationDegrees);

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
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var zone = new ZoneState
                {
                    ZoneTypeId = reader.ReadString(),
                    CenterX = reader.ReadDouble(),
                    CenterZ = reader.ReadDouble(),
                    SideALength = reader.ReadDouble(),
                    SideBLength = reader.ReadDouble(),
                    RotationDegrees = reader.ReadDouble()
                };

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

                _zones.Add(id, zone);
            }
        }

        #endregion
    }
}
