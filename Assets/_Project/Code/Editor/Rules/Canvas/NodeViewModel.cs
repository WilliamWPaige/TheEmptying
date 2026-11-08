using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas
{
    /// <summary>
    /// One node drawn on the canvas: the rule data it comes from, its
    /// resolved block type (null when the block type isn't registered - a
    /// missing-block node), its ports (real or inferred from connections),
    /// read-only field display strings, and its canvas-space bounds.
    /// </summary>
    public sealed class NodeViewModel
    {
        public const float Width = 180f;
        public const float HeaderHeight = 24f;
        public const float PortRowHeight = 18f;
        public const float PortsTopPadding = 6f;
        public const float FieldRowHeight = 14f;
        public const float BottomPadding = 8f;

        public RuleNode Node { get; }

        /// <summary>Null when this node's block type Id isn't registered.</summary>
        public BlockType BlockType { get; }

        public bool IsMissing => BlockType == null;
        public string DisplayName => BlockType?.DisplayName ?? Node.BlockTypeId;
        public BlockKind? Kind => BlockType?.Kind;

        public IReadOnlyList<PortViewModel> Ports { get; }
        public IReadOnlyList<PortViewModel> InputPorts { get; }
        public IReadOnlyList<PortViewModel> OutputPorts { get; }

        /// <summary>Field name to its current read-only display text.</summary>
        public IReadOnlyList<(string Name, string DisplayValue)> FieldSummaries { get; }

        public Vector2 Position => new Vector2((float)Node.X, (float)Node.Y);
        public Vector2 Size { get; }
        public Rect Bounds => new Rect(Position, Size);

        public NodeViewModel(RuleNode node, BlockType blockType, IReadOnlyList<PortViewModel> inferredPorts)
        {
            Node = node;
            BlockType = blockType;

            if (blockType != null)
            {
                Ports = blockType.Ports.Select(p => new PortViewModel(p.Name, p.Direction, p.Kind, p.DataType, isInferred: false)).ToList();
                FieldSummaries = blockType.Fields.Select(f => (f.Name, DescribeField(node, f))).ToList();
            }
            else
            {
                Ports = inferredPorts ?? new List<PortViewModel>();
                FieldSummaries = (node.Fields ?? new Dictionary<string, RuleFieldValueSource>())
                    .Select(kvp => (kvp.Key, DescribeRawField(kvp.Value)))
                    .ToList();
            }

            InputPorts = Ports.Where(p => p.Direction == PortDirection.Input).ToList();
            OutputPorts = Ports.Where(p => p.Direction == PortDirection.Output).ToList();

            int rowCount = System.Math.Max(InputPorts.Count, OutputPorts.Count);
            float height = HeaderHeight + PortsTopPadding + rowCount * PortRowHeight + FieldSummaries.Count * FieldRowHeight + BottomPadding;
            Size = new Vector2(Width, height);

            AssignPortOffsets(InputPorts);
            AssignPortOffsets(OutputPorts);
        }

        private void AssignPortOffsets(IReadOnlyList<PortViewModel> ports)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                float y = HeaderHeight + PortsTopPadding + i * PortRowHeight + PortRowHeight * 0.5f;
                float x = ports[i].Direction == PortDirection.Input ? 0f : Size.x;
                ports[i].LocalOffset = new Vector2(x, y);
            }
        }

        private static string DescribeField(RuleNode node, FieldDefinition field)
        {
            RuleFieldValueSource source = null;
            node.Fields?.TryGetValue(field.Name, out source);
            return source == null ? field.DefaultValue?.ToString() ?? string.Empty : DescribeRawField(source);
        }

        private static string DescribeRawField(RuleFieldValueSource source)
        {
            if (source == null)
            {
                return string.Empty;
            }

            if (source.IsSettingReference)
            {
                return $"[{source.SettingId}]";
            }

            return source.Literal?.ToString() ?? string.Empty;
        }
    }
}
