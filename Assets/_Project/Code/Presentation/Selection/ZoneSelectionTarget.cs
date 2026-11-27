using System;
using System.Collections.Generic;
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
    /// and painted area. A Stockpile shows spots used/total and a row
    /// per known item (amount, a keep-on-hand limit field, and a block
    /// toggle). A Living Area shows how many people are inside it right
    /// now. R21: both have a Modify button (opens the zoning menu with
    /// this zone loaded) and a Delete button, which asks "Delete
    /// "name"? This can't be undone." before actually deleting. A
    /// zone's own on-screen highlight is its tint brightening in place
    /// (ZonePresenter), not the generic highlight quad - Footprint is
    /// never actually drawn from for a zone; see WorldSelectionPresenter.
    /// </summary>
    public sealed class ZoneSelectionTarget : ISelectionTarget
    {
        private readonly ZonesModule _zonesModule;
        private readonly PeopleModule _peopleModule;
        private readonly ContentDatabase _content;
        private readonly MapModule _mapModule;
        private readonly EntityId _id;
        private readonly Action _onRemoved;
        private readonly Action _onZoneChanged;
        private readonly Action<EntityId> _onModify;
        private readonly Action<string> _showToast;

        private bool _confirmingDelete;

        public EntityId Id => _id;

        public ZoneSelectionTarget(ZonesModule zonesModule, PeopleModule peopleModule, ContentDatabase content, MapModule mapModule, EntityId id, Action onRemoved, Action onZoneChanged, Action<EntityId> onModify, Action<string> showToast)
        {
            _zonesModule = zonesModule;
            _peopleModule = peopleModule;
            _content = content;
            _mapModule = mapModule;
            _id = id;
            _onRemoved = onRemoved;
            _onZoneChanged = onZoneChanged;
            _onModify = onModify;
            _showToast = showToast;
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

            string name = string.IsNullOrEmpty(zone.Name) ? DisplayNameForZoneType(zone.ZoneTypeId) : zone.Name;

            if (_confirmingDelete)
            {
                BuildDeleteConfirmContent(container, name);
                return;
            }

            container.Add(new Label(name)
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

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 14 } };
            buttonRow.Add(new Button(RequestModify) { text = "Modify", style = { width = 90, marginRight = 8 } });
            buttonRow.Add(new Button(RequestDelete) { text = "Delete", style = { width = 90 } });
            container.Add(buttonRow);
        }

        private void BuildDeleteConfirmContent(VisualElement container, string name)
        {
            container.Add(new Label($"Delete \"{name}\"? This can't be undone.")
            {
                style = { color = Color.white, whiteSpace = WhiteSpace.Normal, marginBottom = 12 }
            });

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttonRow.Add(new Button(ConfirmDelete) { text = "Yes", style = { width = 90, marginRight = 8 } });
            buttonRow.Add(new Button(CancelDelete) { text = "No", style = { width = 90 } });
            container.Add(buttonRow);
        }

        private double AreaSquareMeters(ZoneState zone)
        {
            double cellSize = _mapModule.CellSizeMeters;
            return zone.Cells.Count * cellSize * cellSize;
        }

        private void BuildStockpileContent(VisualElement container, ZoneState zone)
        {
            int totalSpots = zone.Spots.Count;
            int usedSpots = zone.Spots.Count(s => s.Piles.Count > 0);

            container.Add(new Label($"Spots: {usedSpots} / {totalSpots}")
            {
                style = { color = Color.white, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 6 }
            });

            IEnumerable<string> knownItemIds = zone.Spots
                .SelectMany(s => s.Piles)
                .Select(p => p.ItemContentId)
                .Concat(zone.StockpileItems.Keys)
                .Distinct()
                .OrderBy(id => id, StringComparer.Ordinal);

            foreach (string itemId in knownItemIds.ToArray())
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

        private void RequestModify()
        {
            _onModify?.Invoke(_id);
        }

        private void RequestDelete()
        {
            _confirmingDelete = true;
        }

        private void CancelDelete()
        {
            _confirmingDelete = false;
        }

        private void ConfirmDelete()
        {
            _confirmingDelete = false;
            ZoneDeleteResult result = _zonesModule.DeleteZone(_id);
            _onZoneChanged?.Invoke();

            if (result.FullyDeleted)
            {
                _onRemoved?.Invoke();
                return;
            }

            _showToast?.Invoke($"Could not delete {result.RemainingOccupiedSpots} spot(s) - occupied. Empty them, then delete again.");
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
