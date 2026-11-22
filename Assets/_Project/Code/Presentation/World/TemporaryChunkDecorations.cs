using System.Collections.Generic;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation.Content;
using UnityEngine;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// TEMPORARY (R6b): draws each chunk's randomly-placed trees and
    /// stones (resource.example_tree / resource.example_stone) while that
    /// chunk is loaded, sized to their footprint, through the floating
    /// origin like everything else. A stand-in until the real map design
    /// exists - delete this whole class then.
    /// </summary>
    public sealed class TemporaryChunkDecorations
    {
        private const string TreeContentId = "resource.example_tree";
        private const string StoneContentId = "resource.example_stone";

        private MapModule _mapModule;
        private readonly FloatingOrigin _floatingOrigin;
        private readonly Transform _root;
        private readonly ResourceAsset _treeAsset;
        private readonly ResourceAsset _stoneAsset;

        private readonly Dictionary<ChunkCoord, List<(PlacementKind Kind, GameObject GameObject)>> _activeByChunk =
            new Dictionary<ChunkCoord, List<(PlacementKind Kind, GameObject GameObject)>>();

        private readonly Stack<GameObject> _treePool = new Stack<GameObject>();
        private readonly Stack<GameObject> _stonePool = new Stack<GameObject>();

        public TemporaryChunkDecorations(MapModule mapModule, FloatingOrigin floatingOrigin)
        {
            _mapModule = mapModule;
            _floatingOrigin = floatingOrigin;

            _root = new GameObject("Chunk Decorations (TEMPORARY, R6b)").transform;
            _floatingOrigin.Shifted += delta => _root.position -= delta;

            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            _treeAsset = FindAsset(catalog?.ResourceNodes, TreeContentId);
            _stoneAsset = FindAsset(catalog?.ResourceNodes, StoneContentId);
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
                ResourceAsset asset = placement.Kind == PlacementKind.Tree ? _treeAsset : _stoneAsset;
                if (asset == null || asset.Model == null)
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
                (kind == PlacementKind.Tree ? _treePool : _stonePool).Push(instance);
            }

            _activeByChunk.Remove(coord);
        }

        private GameObject Spawn(PlacementKind kind, ResourceAsset asset)
        {
            Stack<GameObject> pool = kind == PlacementKind.Tree ? _treePool : _stonePool;
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
