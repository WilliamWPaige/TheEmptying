using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Ponds;
using TheEmptying.Presentation.Content;
using UnityEngine;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// R17: draws every pond the player has come near as a flat disc,
    /// scaled to its own random radius and tinted for dry/wet, through
    /// the floating origin. Unlike TemporaryChunkDecorations' trees and
    /// stones, a pond is a real mutable entity (its drink count changes),
    /// so this mirrors it in part (triggers chunk generation as the
    /// camera approaches, so a pond far away stays ungenerated) but
    /// syncs straight from PondsModule.Ponds every frame instead of
    /// pooling per chunk - the entity list is already exactly the set
    /// of ponds worth drawing.
    /// </summary>
    public sealed class PondsPresenter : MonoBehaviour
    {
        private const string PondContentId = "resource.pond";

        private PondsModule _pondsModule;
        private MapModule _mapModule;
        private SettingsRegistry _settings;
        private FloatingOrigin _floatingOrigin;
        private StrategyCameraController _cameraController;

        private GameObject _modelPrefab;
        private Material _wetMaterial;
        private Material _dryMaterial;
        private Transform _root;

        private readonly Dictionary<EntityId, GameObject> _instances = new Dictionary<EntityId, GameObject>();
        private readonly Dictionary<GameObject, EntityId> _entityByInstance = new Dictionary<GameObject, EntityId>();
        private readonly HashSet<EntityId> _seenThisSync = new HashSet<EntityId>();
        private readonly List<EntityId> _toRemove = new List<EntityId>();

        public void Initialize(PondsModule pondsModule, MapModule mapModule, SettingsRegistry settings, FloatingOrigin floatingOrigin, StrategyCameraController cameraController)
        {
            _pondsModule = pondsModule;
            _mapModule = mapModule;
            _settings = settings;
            _floatingOrigin = floatingOrigin;
            _cameraController = cameraController;

            _root = new GameObject("Ponds").transform;
            _floatingOrigin.Shifted += delta => _root.position -= delta;

            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            ResourceAsset asset = FindAsset(catalog?.ResourceNodes, PondContentId);
            _modelPrefab = asset?.Model;

            _wetMaterial = BuildMaterial(new Color(0.18f, 0.42f, 0.72f, 1f));
            _dryMaterial = BuildMaterial(new Color(0.55f, 0.5f, 0.35f, 1f));
        }

        /// <summary>After a load, the simulation (and its PondsModule/MapModule) is a new instance; every drawn pond is destroyed and rebuilt from the new one.</summary>
        public void Rebind(PondsModule pondsModule, MapModule mapModule, SettingsRegistry settings)
        {
            _pondsModule = pondsModule;
            _mapModule = mapModule;
            _settings = settings;

            foreach (GameObject instance in _instances.Values)
            {
                Destroy(instance);
            }

            _instances.Clear();
            _entityByInstance.Clear();
        }

        /// <summary>Resolves a clicked GameObject back to the pond it's currently drawing, if any.</summary>
        public bool TryGetPondForInstance(GameObject instance, out EntityId id) => _entityByInstance.TryGetValue(instance, out id);

        private void Update()
        {
            if (_pondsModule == null || _mapModule == null || _cameraController == null || _modelPrefab == null)
            {
                return;
            }

            QueryChunksNearCamera();
            Sync();
        }

        private void QueryChunksNearCamera()
        {
            Vector3 cameraWorld = _cameraController.FocusPoint;
            ChunkCoord cameraChunk = _mapModule.WorldToChunk(cameraWorld.x, cameraWorld.z);
            int viewDistance = (int)_settings.Get(WorldMapView.ViewDistanceChunksSettingId);

            for (int dz = -viewDistance; dz <= viewDistance; dz++)
            {
                int chunkZ = cameraChunk.Z + dz;
                if (chunkZ < 0 || chunkZ >= _mapModule.ChunkCountZ)
                {
                    continue;
                }

                for (int dx = -viewDistance; dx <= viewDistance; dx++)
                {
                    int chunkX = cameraChunk.X + dx;
                    if (chunkX < 0 || chunkX >= _mapModule.ChunkCountX)
                    {
                        continue;
                    }

                    _pondsModule.GetOrCreatePondsForChunk(new ChunkCoord(chunkX, chunkZ));
                }
            }
        }

        private void Sync()
        {
            _seenThisSync.Clear();

            foreach (KeyValuePair<EntityId, PondState> row in _pondsModule.Ponds)
            {
                _seenThisSync.Add(row.Key);

                if (!_instances.TryGetValue(row.Key, out GameObject instance))
                {
                    instance = Instantiate(_modelPrefab, _root);
                    _instances[row.Key] = instance;
                    _entityByInstance[instance] = row.Key;
                }

                float diameter = (float)(row.Value.RadiusMeters * 2.0);
                Vector3 scale = instance.transform.localScale;
                instance.transform.localScale = new Vector3(diameter, scale.y, diameter);

                var worldPosition = new Vector3((float)row.Value.CenterX, 0f, (float)row.Value.CenterZ);
                instance.transform.position = _floatingOrigin.WorldToRender(worldPosition);

                var renderer = instance.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = row.Value.IsDry ? _dryMaterial : _wetMaterial;
                }
            }

            if (_instances.Count == _seenThisSync.Count)
            {
                return;
            }

            _toRemove.Clear();
            foreach (EntityId id in _instances.Keys)
            {
                if (!_seenThisSync.Contains(id))
                {
                    _toRemove.Add(id);
                }
            }

            foreach (EntityId id in _toRemove)
            {
                GameObject instance = _instances[id];
                _entityByInstance.Remove(instance);
                _instances.Remove(id);
                Destroy(instance);
            }
        }

        private static ResourceAsset FindAsset(IReadOnlyList<ResourceAsset> assets, string id)
        {
            if (assets == null)
            {
                return null;
            }

            foreach (ResourceAsset asset in assets)
            {
                if (asset != null && asset.Id == id)
                {
                    return asset;
                }
            }

            return null;
        }

        private static Material BuildMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { name = "PondMaterial", color = color };
        }
    }
}
