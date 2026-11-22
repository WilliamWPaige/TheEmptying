using System.Collections.Generic;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation.Content;
using UnityEngine;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// TEMPORARY (R6b/R7): draws each chunk's randomly-placed trees,
    /// stones and grub mounds while that chunk is loaded, sized to their
    /// footprint, through the floating origin like everything else. A
    /// stand-in until the real map design exists - delete this whole
    /// class then.
    /// </summary>
    public sealed class TemporaryChunkDecorations
    {
        private const string TreeContentId = "resource.tree";
        private const string StoneContentId = "resource.stone";

        /// <summary>
        /// Grub mounds are the basic food source and never run out - that
        /// rule isn't built yet (gathering adds it later); this is just
        /// where they're placed on the ground.
        /// </summary>
        private const string GrubMoundContentId = "resource.grub_mound";

        private MapModule _mapModule;
        private readonly FloatingOrigin _floatingOrigin;
        private readonly Transform _root;
        private readonly Dictionary<PlacementKind, ResourceAsset> _assetsByKind = new Dictionary<PlacementKind, ResourceAsset>();
        private readonly Dictionary<PlacementKind, Stack<GameObject>> _poolsByKind = new Dictionary<PlacementKind, Stack<GameObject>>();

        private readonly Dictionary<ChunkCoord, List<(PlacementKind Kind, GameObject GameObject)>> _activeByChunk =
            new Dictionary<ChunkCoord, List<(PlacementKind Kind, GameObject GameObject)>>();

        public TemporaryChunkDecorations(MapModule mapModule, FloatingOrigin floatingOrigin)
        {
            _mapModule = mapModule;
            _floatingOrigin = floatingOrigin;

            _root = new GameObject("Chunk Decorations (TEMPORARY, R6b/R7)").transform;
            _floatingOrigin.Shifted += delta => _root.position -= delta;

            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            _assetsByKind[PlacementKind.Tree] = FindAsset(catalog?.ResourceNodes, TreeContentId);
            _assetsByKind[PlacementKind.Stone] = FindAsset(catalog?.ResourceNodes, StoneContentId);
            _assetsByKind[PlacementKind.GrubMound] = FindAsset(catalog?.ResourceNodes, GrubMoundContentId);

            foreach (PlacementKind kind in (PlacementKind[])System.Enum.GetValues(typeof(PlacementKind)))
            {
                _poolsByKind[kind] = new Stack<GameObject>();
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

        /// <summary>After a load, the simulation (and its MapModule) is a new instance; every decoration goes back to its pool.</summary>
        public void Rebind(MapModule mapModule)
        {
            _mapModule = mapModule;

            foreach (ChunkCoord coord in new List<ChunkCoord>(_activeByChunk.Keys))
            {
                HideChunk(coord);
            }
        }

        public void ShowChunk(ChunkCoord coord)
        {
            if (_activeByChunk.ContainsKey(coord))
            {
                return;
            }

            ChunkData chunk = _mapModule.GetOrCreateChunk(coord);
            double cellSize = _mapModule.CellSizeMeters;
            var instances = new List<(PlacementKind Kind, GameObject GameObject)>(chunk.Placements.Count);

            foreach (ChunkPlacement placement in chunk.Placements)
            {
                if (!_assetsByKind.TryGetValue(placement.Kind, out ResourceAsset asset) || asset == null || asset.Model == null)
                {
                    continue;
                }

                GameObject instance = Spawn(placement.Kind, asset);

                var worldPosition = new Vector3((float)(placement.Cell.X * cellSize), 0f, (float)(placement.Cell.Z * cellSize));
                instance.transform.position = _floatingOrigin.WorldToRender(worldPosition);
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, new Vector3(asset.FootprintWidth, 1f, asset.FootprintDepth));
                instance.SetActive(true);

                instances.Add((placement.Kind, instance));
            }

            _activeByChunk[coord] = instances;
        }

        public void HideChunk(ChunkCoord coord)
        {
            if (!_activeByChunk.TryGetValue(coord, out List<(PlacementKind Kind, GameObject GameObject)> instances))
            {
                return;
            }

            foreach ((PlacementKind kind, GameObject instance) in instances)
            {
                instance.SetActive(false);
                _poolsByKind[kind].Push(instance);
            }

            _activeByChunk.Remove(coord);
        }

        private GameObject Spawn(PlacementKind kind, ResourceAsset asset)
        {
            Stack<GameObject> pool = _poolsByKind[kind];
            if (pool.Count > 0)
            {
                GameObject reused = pool.Pop();
                reused.transform.localScale = Vector3.one;
                return reused;
            }

            return Object.Instantiate(asset.Model, _root);
        }
    }
}
