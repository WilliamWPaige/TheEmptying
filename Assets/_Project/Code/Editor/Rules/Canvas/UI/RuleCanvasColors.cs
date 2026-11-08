using TheEmptying.Core.Rules;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>Shared colors: one per block kind (node headers) and one per data type (data ports and their wires), so a type's color always matches between a port and the wires attached to it.</summary>
    internal static class RuleCanvasColors
    {
        public static readonly Color MissingHeader = new Color(0.55f, 0.16f, 0.16f);
        public static readonly Color ExecutionWire = new Color(0.92f, 0.92f, 0.92f);
        public static readonly Color InferredPort = new Color(0.6f, 0.6f, 0.6f);
        public static readonly Color SelectionHighlight = new Color(1f, 0.75f, 0.2f);
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color SelectionBoxFill = new Color(0.3f, 0.6f, 1f, 0.15f);
        public static readonly Color SelectionBoxBorder = new Color(0.3f, 0.6f, 1f, 0.9f);

        public static Color HeaderColor(BlockKind kind)
        {
            return kind switch
            {
                BlockKind.Trigger => new Color(0.75f, 0.35f, 0.35f),
                BlockKind.Value => new Color(0.35f, 0.55f, 0.78f),
                BlockKind.Action => new Color(0.4f, 0.65f, 0.42f),
                BlockKind.Flow => new Color(0.72f, 0.6f, 0.3f),
                _ => Color.gray
            };
        }

        public static Color DataTypeColor(RuleDataType? dataType)
        {
            return dataType switch
            {
                RuleDataType.Number => new Color(0.3f, 0.75f, 0.95f),
                RuleDataType.Bool => new Color(0.85f, 0.4f, 0.8f),
                RuleDataType.Text => new Color(0.95f, 0.75f, 0.25f),
                RuleDataType.Entity => new Color(0.4f, 0.9f, 0.5f),
                RuleDataType.ContentId => new Color(0.7f, 0.55f, 0.95f),
                null => InferredPort,
                _ => Color.gray
            };
        }
    }
}
