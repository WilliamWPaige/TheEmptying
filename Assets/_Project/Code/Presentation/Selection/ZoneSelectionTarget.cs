using System;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Zones;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>
    /// R18b: an info window selection for a placed zone - its name, type
    /// and painted area. A Stockpile shows slots used/total and a row
    /// per known item (amount, a keep-on-hand limit field, and a block
    /// toggle). A Living Area shows how many people are inside it right
    /// now. Both have a "Remove zone" button. A zone's own on-screen
    /// highlight is its tint brightening in place (ZonePresenter), not
    /// the generic highlight quad - Footprint is never actually drawn
    /// from for a zone; see WorldSelectionPresenter.
    /// </summary>
    public sealed class ZoneSelectionTarget : ISelectionTarget
    {
        private readonly ZonesModule _zonesModule;
        private readonly PeopleModule _peopleModule;
        private readonly ContentDatabase _content;
        private readonly MapModule _mapModule;
        private readonly EntityId _id;
        private readonly Action _onRemoved;

        public EntityId Id => _id;

        public ZoneSelectionTarget(ZonesModule zonesModule, PeopleModule peopleModule, ContentDatabase content, MapModule mapModule, EntityId id, Action onRemoved)
        {
            _zonesModule = zonesModule;
            _peopleModule = peopleModule;
            _content = content;
            _mapModule = mapModule;
            _id = id;
            _onRemoved = onRemoved;
        }

        public Vector2 Footprint => Vector2.one;

        public bool TryGetWorldPosition(out Vector3 worldPosition)
        {
            if (_zonesModule.TryGetZone(_id, out ZoneState zone))
            {
                worldPosition = new Vector3((float)zone.CenterX, 0f, (float)zone.CenterZ);
                return true;
            }

            worldPosition = default;
            return false;
        }

        public void BuildContent(VisualElement container)
        {
            if (!_zonesModule.TryGetZone(_id, out ZoneState zone))
            {
                return;
            }

            container.Add(new Label(string.IsNullOrEmpty(zone.Name) ? DisplayNameForZoneType(zone.ZoneTypeId) : zone.Name)
            {
                style = { color = Color.white, fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            });
            container.Add(new Label($"{DisplayNameForZoneType(zone.ZoneTypeId)} - {AreaSquareMeters(zone):0.#} m²")
            {
                style = { color = new Color(0.8f, 0.8f, 0.8f), marginBottom = 10 }
            });

            if (zone.ZoneTypeId == ZonesModule.StockpileTypeId)
            {
                BuildStockpileContent(container, zone);
            }
            else if (zone.ZoneTypeId == ZonesModule.LivingAreaTypeId)
            {
                BuildLivingAreaContent(container, zone);
            }

            container.Add(new Button(RemoveZone) { text = "Remove zone", style = { marginTop = 14, width = 140 } });
        }

        private double AreaSquareMeters(ZoneState zone)
        {
            double cellSize = _mapModule.CellSizeMeters;
            return zone.Cells.Count * cellSize * cellSize;
        }

        private void BuildStockpileContent(VisualElement container, ZoneState zone)
        {
            int totalSlots = _zonesModule.SlotCount(zone.Cells.Count);
            int usedSlots = zone.StockpileItems
                .Where(kv => kv.Value.AmountStored > 0)
                .Sum(kv => ZonesModule.SlotsNeededFor(kv.Value.AmountStored, SizeOf(kv.Key)));

            container.Add(new Label($"Slots: {usedSlots} / {totalSlots}")
            {
                style = { color = Color.white, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 6 }
            });

            foreach (string itemId in zone.StockpileItems.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray())
            {
                container.Add(BuildStockpileItemRow(zone, itemId));
            }
        }

        private VisualElement BuildStockpileItemRow(ZoneState zone, string itemId)
        {
            (int amount, int limit, bool blocked) = _zonesModule.GetStockpileItemInfo(_id, itemId);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };

            row.Add(new Label($"{DisplayNameForItem(itemId)}: {amount}")
            {
                style = { color = new Color(0.85f, 0.85f, 0.85f), width = 140 }
            });

            var limitField = new IntegerField { value = limit, style = { width = 60, marginRight = 6 } };
            limitField.RegisterValueChangedCallback(evt => _zonesModule.SetKeepOnHandLimit(_id, itemId, Mathf.Max(0, evt.newValue)));
            row.Add(limitField);

            row.Add(new Button(() => _zonesModule.SetBlocked(_id, itemId, !blocked))
            {
                text = blocked ? "Unblock" : "Block",
                style = { width = 60 }
            });

            return row;
        }

        private void BuildLivingAreaContent(VisualElement container, ZoneState zone)
        {
            int peopleInside = _peopleModule == null
                ? 0
                : _zonesModule.CountPeopleInside(_id, _peopleModule.People.Select(r => (r.Value.WorldX, r.Value.WorldZ)));

            container.Add(new Label($"People inside: {peopleInside}")
            {
                style = { color = Color.white, unityFontStyleAndWeight = FontStyle.Bold }
            });
        }

        private void RemoveZone()
        {
            _zonesModule.RemoveZone(_id);
            _onRemoved?.Invoke();
        }

        private int SizeOf(string itemContentId)
        {
            if (_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) && entry.Definition is ItemContent item)
            {
                return item.Size;
            }

            return 1;
        }

        private string DisplayNameForItem(string itemContentId)
        {
            if (_content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) && entry.Definition is ItemContent item)
            {
                return item.DisplayName;
            }

            return itemContentId;
        }

        private string DisplayNameForZoneType(string zoneTypeId)
        {
            if (_content.TryGet(BuiltInContentTypes.ZoneTypeTypeName, zoneTypeId, out ContentEntry entry) && entry.Definition is ZoneTypeDefinition zoneType)
            {
                return zoneType.DisplayName;
            }

            return zoneTypeId;
        }
    }
}
