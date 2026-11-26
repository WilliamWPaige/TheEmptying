using System.Collections.Generic;
using TheEmptying.Modules.Buildings;
using TheEmptying.Presentation.Content;
using TheEmptying.Presentation.World;
using UnityEngine;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Buildings
{
    /// <summary>
    /// R11: draws every placed building with its type's model, scaled to
    /// its footprint, through the floating origin. Buildings never move,
    /// get added to or get removed once placed (the player can never
    /// place, move or remove one), so this only ever rebuilds on demand
    /// (after founding, and after a load) - never a per-frame Update().
    /// </summary>
    public sealed class BuildingPresenter : MonoBehaviour
    {
        private FloatingOrigin _floatingOrigin;
        private readonly Dictionary<string, BuildingAsset> _assetsById = new Dictionary<string, BuildingAsset>();
        private readonly Dictionary<EntityId, GameObject> _instances = new Dictionary<EntityId, GameObject>();
        private readonly Dictionary<GameObject, EntityId> _entityByInstance = new Dictionary<GameObject, EntityId>();
        private Transform _root;

        public void Initialize(FloatingOrigin floatingOrigin)
        {
            _floatingOrigin = floatingOrigin;

            _root = new GameObject("Buildings").transform;
            _floatingOrigin.Shifted += delta => _root.position -= delta;

            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            if (catalog != null)
            {
                foreach (BuildingAsset asset in catalog.Buildings)
                {
                    if (asset != null)
                    {
                        _assetsById[asset.Id] = asset;
                    }
                }
            }
        }

        /// <summary>Resolves a clicked GameObject back to the building it's currently drawing, if any.</summary>
        public bool TryGetBuildingForInstance(GameObject instance, out EntityId id) => _entityByInstance.TryGetValue(instance, out id);

        /// <summary>Redraws every placed building from scratch - call once right after founding, and once after a load.</summary>
        public void Rebuild(BuildingsModule buildingsModule)
        {
            foreach (GameObject instance in _instances.Values)
            {
                Destroy(instance);
            }

            _instances.Clear();
            _entityByInstance.Clear();

            foreach (KeyValuePair<EntityId, BuildingState> row in buildingsModule.Buildings)
            {
                BuildingState building = row.Value;
                if (!_assetsById.TryGetValue(building.ContentId, out BuildingAsset asset) || asset.Model == null)
                {
                    continue;
                }

                var worldPosition = new Vector3((float)building.WorldX, 0f, (float)building.WorldZ);
                Vector3 renderPosition = _floatingOrigin.WorldToRender(worldPosition);

                GameObject instance = Object.Instantiate(asset.Model, renderPosition, Quaternion.identity, _root);
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, new Vector3(building.FootprintWidth, 1f, building.FootprintDepth));

                // R20a: the model's own pivot may not be at its base - lift it so its bottom, not its pivot, rests on the ground.
                worldPosition.y = GroundPlacement.PivotHeightAboveBase(instance);
                instance.transform.position = _floatingOrigin.WorldToRender(worldPosition);

                _instances[row.Key] = instance;
                _entityByInstance[instance] = row.Key;
            }
        }
    }
}
