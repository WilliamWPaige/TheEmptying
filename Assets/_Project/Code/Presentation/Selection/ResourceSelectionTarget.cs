using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>R8: an info window selection for one resource placement (tree, stone or grub mound) - display name, cell, footprint and a notes line; R15 adds what can be gathered nearby, for a tree or stone.</summary>
    public sealed class ResourceSelectionTarget : ISelectionTarget
    {
        private readonly MapModule _mapModule;
        private readonly ContentDatabase _content;
        private readonly ResourceAsset _asset;
        private readonly ChunkPlacement _placement;

        public ResourceSelectionTarget(MapModule mapModule, ContentDatabase content, ResourceAsset asset, ChunkPlacement placement)
        {
            _mapModule = mapModule;
            _content = content;
            _asset = asset;
            _placement = placement;
        }

        public Vector2 Footprint => new Vector2(_asset.FootprintWidth, _asset.FootprintDepth);

        /// <summary>The center of the footprint, in true world meters. Resources are permanent once placed (nothing removes them in R8), so this is always valid.</summary>
        public bool TryGetWorldPosition(out Vector3 worldPosition)
        {
            double cellSize = _mapModule.CellSizeMeters;
            double worldX = (_placement.Cell.X + _asset.FootprintWidth * 0.5) * cellSize;
            double worldZ = (_placement.Cell.Z + _asset.FootprintDepth * 0.5) * cellSize;
            worldPosition = new Vector3((float)worldX, 0f, (float)worldZ);
            return true;
        }

        public void BuildContent(VisualElement container)
        {
            container.Add(new Label(_asset.DisplayName)
            {
                style = { color = Color.white, fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 10 }
            });
            container.Add(new Label($"Cell: ({_placement.Cell.X}, {_placement.Cell.Z})") { style = { color = Color.white, marginBottom = 4 } });
            container.Add(new Label($"Footprint: {_asset.FootprintWidth} x {_asset.FootprintDepth}") { style = { color = Color.white, marginBottom = 4 } });

            // Grub mounds are the basic food source and never run out; trees and stones (R15) show what can be gathered nearby instead.
            if (_placement.Kind == PlacementKind.GrubMound)
            {
                container.Add(new Label("Never runs out.") { style = { color = new Color(0.8f, 0.8f, 0.8f), marginTop = 8 } });
            }
            else if (!string.IsNullOrEmpty(_asset.GatherItemId))
            {
                container.Add(new Label($"Gather nearby: {DisplayNameForItem(_asset.GatherItemId)}") { style = { color = new Color(0.8f, 0.8f, 0.8f), marginTop = 8 } });
                container.Add(new Label($"Radius: {_asset.GatherRadiusMeters:0.#} m, up to {_asset.GatherMaxConcurrent} at once") { style = { color = new Color(0.8f, 0.8f, 0.8f) } });
                container.Add(new Label($"Unskilled {_asset.GatherTimeUnskilledMinutes:0.#} min / Skilled {_asset.GatherTimeSkilledMinutes:0.#} min / Master {_asset.GatherTimeMasterMinutes:0.#} min")
                {
                    style = { color = new Color(0.8f, 0.8f, 0.8f) }
                });

                if (!string.IsNullOrEmpty(_asset.GatherBonusItemId))
                {
                    container.Add(new Label($"{_asset.GatherBonusChance * 100:0.#}% chance of {DisplayNameForItem(_asset.GatherBonusItemId)} too") { style = { color = new Color(0.8f, 0.8f, 0.8f) } });
                }
            }
        }

        private string DisplayNameForItem(string itemContentId)
        {
            if (_content != null && _content.TryGet(BuiltInContentTypes.ItemTypeName, itemContentId, out ContentEntry entry) && entry.Definition is ItemContent item)
            {
                return item.DisplayName;
            }

            return itemContentId;
        }
    }
}
