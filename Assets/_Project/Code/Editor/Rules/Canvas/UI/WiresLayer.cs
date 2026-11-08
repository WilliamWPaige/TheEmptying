using TheEmptying.Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>Draws every wire as a curve between its two ports, in screen space (via the view-model's CanvasToScreen). Execution wires draw thicker and neutral-colored; data wires draw in their type's color.</summary>
    internal sealed class WiresLayer : VisualElement
    {
        private const float MinExecutionWidth = 3f;
        private const float MinDataWidth = 2f;

        private readonly RuleCanvasViewModel _viewModel;

        public WiresLayer(RuleCanvasViewModel viewModel)
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
            Painter2D painter = mgc.painter2D;

            foreach (WireViewModel wire in _viewModel.Wires)
            {
                bool isExecution = wire.Kind == PortKind.Execution;
                painter.strokeColor = isExecution ? RuleCanvasColors.ExecutionWire : RuleCanvasColors.DataTypeColor(wire.DataType);
                painter.lineWidth = isExecution ? MinExecutionWidth : MinDataWidth;

                Vector2 from = _viewModel.CanvasToScreen(wire.FromAnchor);
                Vector2 to = _viewModel.CanvasToScreen(wire.ToAnchor);
                float tangent = Mathf.Max(40f, Mathf.Abs(to.x - from.x) * 0.5f);

                painter.BeginPath();
                painter.MoveTo(from);
                painter.BezierCurveTo(from + new Vector2(tangent, 0f), to - new Vector2(tangent, 0f), to);
                painter.Stroke();
            }
        }

        public void Refresh() => MarkDirtyRepaint();
    }
}
