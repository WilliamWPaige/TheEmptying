using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Editing;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas
{
    /// <summary>
    /// One node drawn on the canvas: the rule data it comes from, its
    /// resolved block type (null when the block type isn't registered - a
    /// missing-block node), its ports (real or inferred from connections),
    /// read-only field display strings, and its canvas-space bounds.
    ///
    /// Every position a NodeElement draws (port rows, port labels, the
    /// family variant dropdown, field rows, and the node's own size) is
    /// computed here, not in NodeElement, so the layout itself is covered
    /// by EditMode tests without needing a live window.
    /// </summary>
    public sealed class NodeViewModel
    {
        public const float Width = 200f;
        public const float HeaderHeight = 24f;
        public const float PortRowHeight = 18f;
        public const float PortsTopPadding = 6f;
        public const float FieldRowHeight = 14f;
        public const float FieldRowSpacing = 2f;
        public const float BottomPadding = 8f;

        /// <summary>A port's label box is exactly as tall as its row, so centering the label vertically in this box always lines it up with its dot (which sits at the row's own center).</summary>
        public const float PortLabelHeight = PortRowHeight;

        private const float MinLabelColumnWidth = 44f;
        private const float MaxLabelColumnWidth = 110f;
        private const float LabelColumnCharWidth = 5.5f;
        private const float LabelColumnPadding = 8f;

        public RuleNode Node { get; }

        /// <summary>Null when this node's block type Id isn't registered.</summary>
        public BlockType BlockType { get; }

        /// <summary>Null unless this node's block type belongs to a detected family (Get Local, Set Local, Read Event Parameter, ...), in which case it gets a type dropdown.</summary>
        public BlockFamily Family { get; }

        public bool IsMissing => BlockType == null;
        public bool HasVariantDropdown => !IsMissing && Family != null;
        public string DisplayName => BlockType?.DisplayName ?? Node.BlockTypeId;
        public BlockKind? Kind => BlockType?.Kind;

        /// <summary>How wide the field-name label column must be to fit the longest field name on any block in the registry this node was built from. Shared by every node on the canvas (computed once by RuleCanvasViewModel.Rebuild).</summary>
        public float LabelColumnWidth { get; }

        public IReadOnlyList<PortViewModel> Ports { get; }
        public IReadOnlyList<PortViewModel> InputPorts { get; }
        public IReadOnlyList<PortViewModel> OutputPorts { get; }

        /// <summary>Field name to its current read-only display text (missing-block nodes only).</summary>
        public IReadOnlyList<(string Name, string DisplayValue)> FieldSummaries { get; }

        /// <summary>Top Y (canvas-local) of the family variant dropdown row. Only meaningful when HasVariantDropdown.</summary>
        public float VariantDropdownY { get; }

        /// <summary>Top Y (canvas-local) of each field row, one per BlockType.Fields entry (or FieldSummaries entry for a missing block), in the same order.</summary>
        public IReadOnlyList<float> FieldRowY { get; }

        public Vector2 Position => new Vector2((float)Node.X, (float)Node.Y);
        public Vector2 Size { get; }
        public Rect Bounds => new Rect(Position, Size);

        public NodeViewModel(RuleNode node, BlockType blockType, IReadOnlyList<PortViewModel> inferredPorts, BlockFamily family, float labelColumnWidth)
        {
            Node = node;
            BlockType = blockType;
            Family = family;
            LabelColumnWidth = labelColumnWidth;

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

            AssignPortOffsets(InputPorts);
            AssignPortOffsets(OutputPorts);

            int rowCount = Math.Max(InputPorts.Count, OutputPorts.Count);
            float contentTop = HeaderHeight + PortsTopPadding + rowCount * PortRowHeight;

            VariantDropdownY = contentTop;
            if (HasVariantDropdown)
            {
                contentTop += FieldRowHeight + FieldRowSpacing;
            }

            int fieldRowCount = FieldSummaries.Count;
            var fieldRowY = new List<float>(fieldRowCount);
            for (int i = 0; i < fieldRowCount; i++)
            {
                fieldRowY.Add(contentTop + i * (FieldRowHeight + FieldRowSpacing));
            }

            FieldRowY = fieldRowY;

            float bottom = contentTop + fieldRowCount * (FieldRowHeight + FieldRowSpacing) + BottomPadding;
            Size = new Vector2(Width, bottom);
        }

        /// <summary>Where a port's label box's top edge sits so it's centered on the same row as its dot (dot center == port.LocalOffset.y).</summary>
        public static float PortLabelTop(PortViewModel port) => port.LocalOffset.y - PortLabelHeight * 0.5f;

        /// <summary>How wide the field-name label column must be to fit the longest field name across every block type in blocks (clamped to a sane range so one long name can't make every node absurdly wide).</summary>
        public static float ComputeLabelColumnWidth(BlockRegistry blocks)
        {
            int longest = 0;
            foreach (IGrouping<string, BlockType> group in blocks.ListByCategory())
            {
                foreach (BlockType blockType in group)
                {
                    foreach (FieldDefinition field in blockType.Fields)
                    {
                        longest = Math.Max(longest, field.Name.Length);
                    }
                }
            }

            float width = longest * LabelColumnCharWidth + LabelColumnPadding;
            return Mathf.Clamp(width, MinLabelColumnWidth, MaxLabelColumnWidth);
        }

        private static void AssignPortOffsets(IReadOnlyList<PortViewModel> ports)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                float y = HeaderHeight + PortsTopPadding + i * PortRowHeight + PortRowHeight * 0.5f;
                float x = ports[i].Direction == PortDirection.Input ? 0f : Width;
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
