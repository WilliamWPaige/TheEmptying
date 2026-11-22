using System;
using System.Collections.Generic;
using TheEmptying.Presentation.Content;
using TheEmptying.Presentation.World;
using UnityEngine;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>
    /// TEST TOOL (R5): lists every loaded unit, resource, building and item
    /// in the cheat menu, grouped by type; clicking one spawns its Model on
    /// the ground at the camera's center, sized to its footprint for
    /// buildings and resources. Purely visual - it never touches the
    /// simulation, and nothing it spawns is saved. "Clear spawned" removes
    /// everything it has placed.
    /// </summary>
    public sealed class SpawnCheat
    {
        private readonly ContentCatalog _catalog;
        private readonly StrategyCameraController _cameraController;
        private readonly Transform _spawnParent;

        public SpawnCheat(StrategyCameraController cameraController)
        {
            _cameraController = cameraController;
            _catalog = Resources.Load<ContentCatalog>("ContentCatalog");

            _spawnParent = new GameObject("Spawned (test tool, not saved)").transform;
        }

        /// <summary>Adds the "Spawn" sections to the given registry. Call again after every rebuild (e.g. after a Load), same as the Time and Settings sections.</summary>
        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection spawn = registry.AddSection("Spawn (test tool)");
            spawn.AddButton("Clear spawned", ClearSpawned);

            if (_catalog == null)
            {
                return;
            }

            AddTypeSection(registry, "Spawn: Units", _catalog.Units, asset => Spawn(asset, Vector3.one));
            AddTypeSection(registry, "Spawn: Resources", _catalog.ResourceNodes, asset => Spawn(asset, new Vector3(asset.FootprintWidth, 1f, asset.FootprintDepth)));
            AddTypeSection(registry, "Spawn: Buildings", _catalog.Buildings, asset => Spawn(asset, new Vector3(asset.FootprintWidth, 1f, asset.FootprintDepth)));
            AddTypeSection(registry, "Spawn: Items", _catalog.Items, asset => Spawn(asset, Vector3.one));
        }

        private static void AddTypeSection<TAsset>(CheatMenuRegistry registry, string title, IReadOnlyList<TAsset> assets, Action<TAsset> spawn)
            where TAsset : ContentAssetBase
        {
            if (assets.Count == 0)
            {
                return;
            }

            CheatSection section = registry.AddSection(title);
            foreach (TAsset asset in assets)
            {
                if (asset == null || asset.Model == null)
                {
                    continue;
                }

                TAsset captured = asset;
                section.AddButton(asset.Id, () => spawn(captured));
            }
        }

        private void Spawn(ContentAssetBase asset, Vector3 footprintScale)
        {
            Vector3 position = _cameraController != null ? _cameraController.FocusPoint : Vector3.zero;
            GameObject instance = UnityEngine.Object.Instantiate(asset.Model, position, Quaternion.identity, _spawnParent);
            instance.name = asset.Id;
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, footprintScale);
        }

        private void ClearSpawned()
        {
            for (int i = _spawnParent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_spawnParent.GetChild(i).gameObject);
            }
        }
    }
}
