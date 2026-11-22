using System.Collections.Generic;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Presentation.Content;
using TheEmptying.Presentation.World;
using UnityEngine;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.People
{
    /// <summary>
    /// R7/R8: draws each living person with its type's model, through the
    /// floating origin, only while their chunk is loaded. Selection and
    /// the info window live in WorldSelectionPresenter, which uses
    /// TryGetEntityForInstance to tell a clicked person apart from
    /// anything else.
    /// </summary>
    public sealed class PersonPresenter : MonoBehaviour
    {
        private PeopleModule _peopleModule;
        private MapModule _mapModule;
        private FloatingOrigin _floatingOrigin;
        private WorldMapView _worldMapView;

        private readonly Dictionary<string, UnitAsset> _unitAssetsById = new Dictionary<string, UnitAsset>();
        private readonly Dictionary<EntityId, GameObject> _activeInstances = new Dictionary<EntityId, GameObject>();
        private readonly Dictionary<GameObject, EntityId> _entityByInstance = new Dictionary<GameObject, EntityId>();
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();
        private readonly List<EntityId> _toHide = new List<EntityId>();

        private Transform _root;

        public void Initialize(PeopleModule peopleModule, MapModule mapModule, FloatingOrigin floatingOrigin, WorldMapView worldMapView)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;
            _floatingOrigin = floatingOrigin;
            _worldMapView = worldMapView;

            _root = new GameObject("People").transform;

            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            if (catalog != null)
            {
                foreach (UnitAsset asset in catalog.Units)
                {
                    if (asset != null)
                    {
                        _unitAssetsById[asset.Id] = asset;
                    }
                }
            }
        }

        /// <summary>After a load, the simulation (and its modules) are new instances; every instance goes back to the pool, since the old EntityIds no longer mean anything.</summary>
        public void Rebind(PeopleModule peopleModule, MapModule mapModule)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;

            foreach (GameObject instance in _activeInstances.Values)
            {
                instance.SetActive(false);
                _pool.Push(instance);
            }

            _activeInstances.Clear();
            _entityByInstance.Clear();
        }

        /// <summary>Resolves a clicked GameObject back to the person it's currently drawing, if any.</summary>
        public bool TryGetEntityForInstance(GameObject instance, out EntityId id) => _entityByInstance.TryGetValue(instance, out id);

        private void Update()
        {
            if (_peopleModule == null)
            {
                return;
            }

            UpdateInstances();
        }

        private void UpdateInstances()
        {
            var seen = new HashSet<EntityId>();

            foreach (KeyValuePair<EntityId, PersonState> row in _peopleModule.People)
            {
                EntityId id = row.Key;
                PersonState person = row.Value;
                seen.Add(id);

                ChunkCoord chunk = _mapModule.WorldToChunk(person.WorldX, person.WorldZ);
                if (!_worldMapView.IsChunkLoaded(chunk))
                {
                    HideInstance(id);
                    continue;
                }

                if (!_activeInstances.TryGetValue(id, out GameObject instance))
                {
                    if (!_unitAssetsById.TryGetValue(person.UnitContentId, out UnitAsset asset) || asset.Model == null)
                    {
                        continue;
                    }

                    instance = _pool.Count > 0 ? _pool.Pop() : Object.Instantiate(asset.Model, _root);
                    instance.SetActive(true);
                    _activeInstances[id] = instance;
                    _entityByInstance[instance] = id;
                }

                var worldPosition = new Vector3((float)person.WorldX, 0f, (float)person.WorldZ);
                instance.transform.position = _floatingOrigin.WorldToRender(worldPosition);
            }

            // Anyone no longer alive (removed or destroyed) also goes back to the pool.
            _toHide.Clear();
            foreach (EntityId id in _activeInstances.Keys)
            {
                if (!seen.Contains(id))
                {
                    _toHide.Add(id);
                }
            }

            foreach (EntityId id in _toHide)
            {
                HideInstance(id);
            }
        }

        private void HideInstance(EntityId id)
        {
            if (!_activeInstances.TryGetValue(id, out GameObject instance))
            {
                return;
            }

            instance.SetActive(false);
            _activeInstances.Remove(id);
            _entityByInstance.Remove(instance);
            _pool.Push(instance);
        }
    }
}
