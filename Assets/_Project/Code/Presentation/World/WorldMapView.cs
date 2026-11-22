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
    /// TemporaryGround. Flat and plain for now, with a faint grid; swaps
    /// to a flat material with no grid lines when zoomed far out, so
    /// performance stays smooth.
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour
    {
        public const string ViewDistanceChunksSettingId = "setting.map.view_distance_chunks";
        public const string LowDetailHeightSettingId = "setting.map.low_detail_height_meters";

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
        private Material _gridMaterial;
        private Material _flatMaterial;
        private Material _currentMaterial;

        private readonly Dictionary<ChunkCoord, ChunkTile> _activeTiles = new Dictionary<ChunkCoord, ChunkTile>();
        private readonly Stack<ChunkTile> _pool = new Stack<ChunkTile>();
        private readonly List<ChunkCoord> _toRemove = new List<ChunkCoord>();
        private readonly HashSet<ChunkCoord> _needed = new HashSet<ChunkCoord>();

        /// <summary>The Map cheat's grid on/off toggle. The low-detail zoom view always hides the grid regardless of this.</summary>
        public bool GridEnabledByPlayer { get; set; } = true;

        public void Initialize(MapModule mapModule, SettingsRegistry settings, FloatingOrigin floatingOrigin, StrategyCameraController cameraController)
        {
            _mapModule = mapModule;
            _settings = settings;
            _floatingOrigin = floatingOrigin;
            _cameraController = cameraController;

            _chunkRoot = new GameObject("World Chunks").transform;
            _floatingOrigin.Shifted += delta => _chunkRoot.position -= delta;
            _decorations = new TemporaryChunkDecorations(mapModule, floatingOrigin);

            float cellsPerSide = (float)(mapModule.ChunkSizeMeters / mapModule.CellSizeMeters);
            _tileMesh = BuildTileMesh((float)mapModule.ChunkSizeMeters);
            _gridMaterial = BuildMaterial(withGrid: true, cellsPerSide);
            _flatMaterial = BuildMaterial(withGrid: false, cellsPerSide);
            _currentMaterial = _gridMaterial;
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

            UpdateMaterial();
            UpdateActiveChunks();
        }

        private void UpdateMaterial()
        {
            bool lowDetail = _cameraController.Height >= _settings.Get(LowDetailHeightSettingId);
            Material material = (!GridEnabledByPlayer || lowDetail) ? _flatMaterial : _gridMaterial;
            if (material == _currentMaterial)
            {
                return;
            }

            _currentMaterial = material;
            foreach (ChunkTile tile in _activeTiles.Values)
            {
                tile.Renderer.sharedMaterial = material;
            }
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
                tile.Renderer.sharedMaterial = _currentMaterial;
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
            meshRenderer.sharedMaterial = _currentMaterial;
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

        /// <summary>An unlit material with (or without) a small generated grid texture, tiled once per cell.</summary>
        private static Material BuildMaterial(bool withGrid, float cellsPerSide)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = withGrid ? "WorldChunkGridMaterial" : "WorldChunkFlatMaterial" };

            Texture2D texture = BuildGridTexture(withGrid);
            material.mainTexture = texture;
            material.mainTextureScale = withGrid ? new Vector2(cellsPerSide, cellsPerSide) : Vector2.one;

            return material;
        }

        private static Texture2D BuildGridTexture(bool withGrid)
        {
            const int size = 8;
            var groundColor = new Color(0.33f, 0.36f, 0.33f);
            var lineColor = new Color(0.27f, 0.3f, 0.27f);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = withGrid ? "WorldChunkGridTexture" : "WorldChunkFlatTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onLine = withGrid && (x == 0 || y == 0);
                    texture.SetPixel(x, y, onLine ? lineColor : groundColor);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
