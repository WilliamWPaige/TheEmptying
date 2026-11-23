using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.World;
using UnityEngine;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R12: draws every placed zone as a tinted, outlined ground
    /// rectangle, color per zone type, through the floating origin.
    /// Zones never move once placed, so this only rebuilds on demand
    /// (after placing or removing one, and after a load) - not a
    /// per-frame Update(). R12b: the selected zone's own outline is
    /// brightened in place instead of a separate highlight shape.
    /// </summary>
    public sealed class ZonePresenter : MonoBehaviour
    {
        private static readonly Color SelectedTint = new Color(1f, 1f, 1f);

        private FloatingOrigin _floatingOrigin;
        private ContentDatabase _content;
        private Transform _root;

        private readonly Dictionary<EntityId, ZoneVisual> _instances = new Dictionary<EntityId, ZoneVisual>();
        private readonly Dictionary<EntityId, string> _zoneTypeById = new Dictionary<EntityId, string>();
        private readonly Dictionary<GameObject, EntityId> _entityByInstance = new Dictionary<GameObject, EntityId>();

        private EntityId? _selectedZoneId;

        public void Initialize(FloatingOrigin floatingOrigin, ContentDatabase content)
        {
            _floatingOrigin = floatingOrigin;
            _content = content;

            _root = new GameObject("Zones").transform;
            _floatingOrigin.Shifted += delta => _root.position -= delta;
        }

        public bool TryGetZoneForInstance(GameObject instance, out EntityId id) => _entityByInstance.TryGetValue(instance, out id);

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

        /// <summary>Rebinds to a new (post-load) content database and redraws everything.</summary>
        public void Rebind(ContentDatabase content)
        {
            _content = content;
        }

        /// <summary>R12b: brightens the given zone's own outline in place (no separate highlight shape for zones); null clears it. Survives the next Rebuild as long as the zone still exists.</summary>
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
            if (!_instances.TryGetValue(id, out ZoneVisual visual) || !_zoneTypeById.TryGetValue(id, out string zoneTypeId))
            {
                return;
            }

            Color baseColor = ColorFor(zoneTypeId);
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
            _zoneTypeById.Clear();

            foreach (KeyValuePair<EntityId, ZoneState> row in zonesModule.Zones)
            {
                var visual = new ZoneVisual(_root, ColorFor(row.Value.ZoneTypeId), collidable: true);
                visual.UpdateGeometry(row.Value.Rect, _floatingOrigin);

                _instances[row.Key] = visual;
                _zoneTypeById[row.Key] = row.Value.ZoneTypeId;
                _entityByInstance[visual.GameObject] = row.Key;
            }

            if (_selectedZoneId.HasValue)
            {
                ApplySelectionColor(_selectedZoneId.Value, selected: true);
            }
        }
    }
}
