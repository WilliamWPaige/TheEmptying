using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// R6: the chunked ground. Only chunks near the camera are built and
    /// shown - built tiles are pooled (hidden and reused, never destroyed)
    /// so panning across a chunk edge never hitches. Replaces the R1
    /// TemporaryGround. R12: flat and plain, with no grid - the grid
    /// display was removed; it wasn't needed.
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour
    {
        public const string ViewDistanceChunksSettingId = "setting.map.view_distance_chunks";

        private sealed class ChunkTile
        {
            public GameObject GameObject;
            public MeshRenderer Renderer;
        }

        private MapModule _mapModule;
        private SettingsRegistry _settings;
        private FloatingOrigin _floatingOrigin;
        private StrategyCameraController _cameraController;
        private TemporaryChunkDecorations _decorations;

        private Transform _chunkRoot;
        private Mesh _tileMesh;
        private Material _groundMaterial;

        private readonly Dictionary<ChunkCoord, ChunkTile> _activeTiles = new Dictionary<ChunkCoord, ChunkTile>();
        private readonly Stack<ChunkTile> _pool = new Stack<ChunkTile>();
        private readonly List<ChunkCoord> _toRemove = new List<ChunkCoord>();
        private readonly HashSet<ChunkCoord> _needed = new HashSet<ChunkCoord>();

        /// <summary>For the world selection/click router (R8), to resolve a clicked resource back to its placement.</summary>
        public TemporaryChunkDecorations Decorations => _decorations;

        /// <summary>Whether this chunk currently has a built, visible ground tile - the same "loaded" every other per-chunk drawing (decorations, people) goes by.</summary>
        public bool IsChunkLoaded(ChunkCoord coord) => _activeTiles.ContainsKey(coord);

        public void Initialize(MapModule mapModule, SettingsRegistry settings, FloatingOrigin floatingOrigin, StrategyCameraController cameraController)
        {
            _mapModule = mapModule;
            _settings = settings;
            _floatingOrigin = floatingOrigin;
            _cameraController = cameraController;

            _chunkRoot = new GameObject("World Chunks").transform;
            _floatingOrigin.Shifted += delta => _chunkRoot.position -= delta;
            _decorations = new TemporaryChunkDecorations(mapModule, floatingOrigin);

            _tileMesh = BuildTileMesh((float)mapModule.ChunkSizeMeters);
            _groundMaterial = BuildMaterial();
        }

        /// <summary>After a load, the simulation (and its MapModule/SettingsRegistry) is a new instance; every tile goes back to the pool and is rebuilt from the new one next Update.</summary>
        public void Rebind(MapModule mapModule, SettingsRegistry settings)
        {
            _mapModule = mapModule;
            _settings = settings;
            _decorations.Rebind(mapModule);

            foreach (ChunkTile tile in _activeTiles.Values)
            {
                tile.GameObject.SetActive(false);
                _pool.Push(tile);
            }

            _activeTiles.Clear();
        }

        private void Update()
        {
            if (_mapModule == null || _cameraController == null)
            {
                return;
            }

            UpdateActiveChunks();
        }

        private void UpdateActiveChunks()
        {
            Vector3 cameraWorld = _cameraController.FocusPoint;
            ChunkCoord cameraChunk = _mapModule.WorldToChunk(cameraWorld.x, cameraWorld.z);
            int viewDistance = (int)_settings.Get(ViewDistanceChunksSettingId);

            _needed.Clear();
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

                    _needed.Add(new ChunkCoord(chunkX, chunkZ));
                }
            }

            _toRemove.Clear();
            foreach (ChunkCoord active in _activeTiles.Keys)
            {
                if (!_needed.Contains(active))
                {
                    _toRemove.Add(active);
                }
            }

            foreach (ChunkCoord coord in _toRemove)
            {
                ChunkTile tile = _activeTiles[coord];
                tile.GameObject.SetActive(false);
                _pool.Push(tile);
                _activeTiles.Remove(coord);
                _decorations.HideChunk(coord);
            }

            foreach (ChunkCoord coord in _needed)
            {
                if (_activeTiles.ContainsKey(coord))
                {
                    continue;
                }

                // Lazily creates this chunk's simulation-side data (and its one-time random placements) the first time it's needed (R6/R6b) - never a fixed array for the whole world.
                _mapModule.GetOrCreateChunk(coord);

                ChunkTile tile = _pool.Count > 0 ? _pool.Pop() : BuildTile();
                (double originX, double originZ) = _mapModule.ChunkOrigin(coord);
                var worldOrigin = new Vector3((float)originX, 0f, (float)originZ);
                tile.GameObject.transform.position = _floatingOrigin.WorldToRender(worldOrigin);
                tile.GameObject.SetActive(true);
                _activeTiles[coord] = tile;

                _decorations.ShowChunk(coord);
            }
        }

        private ChunkTile BuildTile()
        {
            var gameObject = new GameObject("Chunk");
            gameObject.transform.SetParent(_chunkRoot, worldPositionStays: true);

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = _tileMesh;

            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _groundMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            return new ChunkTile { GameObject = gameObject, Renderer = meshRenderer };
        }

        private static Mesh BuildTileMesh(float size)
        {
            var mesh = new Mesh { name = "WorldChunkMesh" };

            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, size),
                new Vector3(size, 0f, size),
                new Vector3(size, 0f, 0f)
            };

            var uvs = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };

            var triangles = new[] { 0, 1, 2, 0, 2, 3 };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>A single plain, unlit ground color - no grid.</summary>
        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            return new Material(shader) { name = "WorldChunkGroundMaterial", color = new Color(0.33f, 0.36f, 0.33f) };
        }
    }
}
