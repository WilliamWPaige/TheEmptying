using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.World;
using UnityEngine;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18b: draws every placed zone as its own painted cells (which may
    /// be several separate pieces), tinted with the zone's own stored
    /// color, through the floating origin. Zones never move once
    /// placed, so this only rebuilds on demand (after placing or
    /// removing one, and after a load) - not a per-frame Update(). The
    /// selected zone's own tint is brightened in place instead of a
    /// separate highlight shape. R18b's Overlays "Toggle zones" option
    /// uses SetVisible; the zoning view forces it back on regardless
    /// (see GameBootstrap).
    /// </summary>
    public sealed class ZonePresenter : MonoBehaviour
    {
        /// <summary>A painted zone's tint strength - not a setting, a plain visual constant like the other colors in Presentation. Public so the zoning menu's live paint preview (ZoningMenuPresenter) matches it exactly.</summary>
        public const float TintAlpha = 0.45f;

        private static readonly Color SelectedTint = new Color(1f, 1f, 1f, TintAlpha);

        private FloatingOrigin _floatingOrigin;
        private ContentDatabase _content;
        private MapModule _mapModule;
        private Transform _root;

        private readonly Dictionary<EntityId, ZoneVisual> _instances = new Dictionary<EntityId, ZoneVisual>();
        private readonly Dictionary<EntityId, Color> _baseColorById = new Dictionary<EntityId, Color>();
        private readonly Dictionary<GameObject, EntityId> _entityByInstance = new Dictionary<GameObject, EntityId>();

        private EntityId? _selectedZoneId;

        public void Initialize(FloatingOrigin floatingOrigin, ContentDatabase content, MapModule mapModule)
        {
            _floatingOrigin = floatingOrigin;
            _content = content;
            _mapModule = mapModule;

            _root = new GameObject("Zones").transform;
            _floatingOrigin.Shifted += delta => _root.position -= delta;
        }

        public bool TryGetZoneForInstance(GameObject instance, out EntityId id) => _entityByInstance.TryGetValue(instance, out id);

        /// <summary>A zone TYPE's own default tint (the menu's "Color" default) - not a placed zone's own stored color, see ColorForZone.</summary>
        public Color ColorFor(string zoneTypeId)
        {
            if (_content != null && _content.TryGet(BuiltInContentTypes.ZoneTypeTypeName, zoneTypeId, out ContentEntry entry) && entry.Definition is ZoneTypeDefinition zoneType)
            {
                if (ColorUtility.TryParseHtmlString(zoneType.ColorHex, out Color parsed))
                {
                    return parsed;
                }
            }

            return Color.gray;
        }

        /// <summary>A placed zone's own stored tint, falling back to its type's default if the stored hex is missing or invalid.</summary>
        private Color ColorForZone(ZoneState zone)
        {
            Color baseColor = ColorUtility.TryParseHtmlString(zone.ColorHex, out Color parsed) ? parsed : ColorFor(zone.ZoneTypeId);
            return new Color(baseColor.r, baseColor.g, baseColor.b, TintAlpha);
        }

        /// <summary>Rebinds to a new (post-load) content database/map and redraws everything.</summary>
        public void Rebind(ContentDatabase content, MapModule mapModule)
        {
            _content = content;
            _mapModule = mapModule;
        }

        /// <summary>R18b: the Overlays "Toggle zones" option - hides or shows every placed zone's tint and outline at once.</summary>
        public void SetVisible(bool visible) => _root.gameObject.SetActive(visible);

        /// <summary>Brightens the given zone's own tint in place; null clears it. Survives the next Rebuild as long as the zone still exists.</summary>
        public void SetSelected(EntityId? id)
        {
            if (_selectedZoneId.HasValue)
            {
                ApplySelectionColor(_selectedZoneId.Value, selected: false);
            }

            _selectedZoneId = id;

            if (_selectedZoneId.HasValue)
            {
                ApplySelectionColor(_selectedZoneId.Value, selected: true);
            }
        }

        private void ApplySelectionColor(EntityId id, bool selected)
        {
            if (!_instances.TryGetValue(id, out ZoneVisual visual) || !_baseColorById.TryGetValue(id, out Color baseColor))
            {
                return;
            }

            visual.SetColor(selected ? Color.Lerp(baseColor, SelectedTint, 0.5f) : baseColor);
        }

        public void Rebuild(ZonesModule zonesModule)
        {
            foreach (ZoneVisual visual in _instances.Values)
            {
                _entityByInstance.Remove(visual.GameObject);
                visual.Destroy();
            }

            _instances.Clear();
            _baseColorById.Clear();

            double cellSize = _mapModule.CellSizeMeters;
            foreach (KeyValuePair<EntityId, ZoneState> row in zonesModule.Zones)
            {
                Color color = ColorForZone(row.Value);
                var visual = new ZoneVisual(_root, color, collidable: true);
                visual.UpdateGeometry(row.Value.Cells, cellSize, _floatingOrigin);

                _instances[row.Key] = visual;
                _baseColorById[row.Key] = color;
                _entityByInstance[visual.GameObject] = row.Key;
            }

            if (_selectedZoneId.HasValue)
            {
                ApplySelectionColor(_selectedZoneId.Value, selected: true);
            }
        }
    }
}
