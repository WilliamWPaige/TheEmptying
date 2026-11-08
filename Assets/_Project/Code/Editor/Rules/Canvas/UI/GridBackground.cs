using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>A grid that scales with zoom, drawn directly in screen space from the view-model's current pan and zoom (no transform tricks).</summary>
    internal sealed class GridBackground : VisualElement
    {
        private const float BaseSpacing = 24f;
        private const float MinDrawnSpacing = 6f;

        private readonly RuleCanvasViewModel _viewModel;

        public GridBackground(RuleCanvasViewModel viewModel)
        {
            _viewModel = viewModel;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float spacing = BaseSpacing * _viewModel.Zoom;
            if (spacing < MinDrawnSpacing)
            {
                return;
            }

            Painter2D painter = mgc.painter2D;
            painter.strokeColor = RuleCanvasColors.GridLine;
            painter.lineWidth = 1f;

            float offsetX = Mod(_viewModel.Pan.x, spacing);
            float offsetY = Mod(_viewModel.Pan.y, spacing);

            painter.BeginPath();
            for (float x = offsetX; x < rect.width; x += spacing)
            {
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, rect.height));
            }

            for (float y = offsetY; y < rect.height; y += spacing)
            {
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(rect.width, y));
            }

            painter.Stroke();
        }

        private static float Mod(float value, float modulus)
        {
            float result = value % modulus;
            return result < 0f ? result + modulus : result;
        }

        public void Refresh() => MarkDirtyRepaint();
    }
}
