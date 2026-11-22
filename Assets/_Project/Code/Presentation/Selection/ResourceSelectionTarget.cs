using TheEmptying.Modules.Map;
using TheEmptying.Presentation.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>R8: an info window selection for one resource placement (tree, stone or grub mound) - display name, cell, footprint and a notes line.</summary>
    public sealed class ResourceSelectionTarget : ISelectionTarget
    {
        private readonly MapModule _mapModule;
        private readonly ResourceAsset _asset;
        private readonly ChunkPlacement _placement;

        public ResourceSelectionTarget(MapModule mapModule, ResourceAsset asset, ChunkPlacement placement)
        {
            _mapModule = mapModule;
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

            // Trees and stones have no notes yet; grub mounds are the basic food source and never run out.
            if (_placement.Kind == PlacementKind.GrubMound)
            {
                container.Add(new Label("Never runs out.") { style = { color = new Color(0.8f, 0.8f, 0.8f), marginTop = 8 } });
            }
        }
    }
}
