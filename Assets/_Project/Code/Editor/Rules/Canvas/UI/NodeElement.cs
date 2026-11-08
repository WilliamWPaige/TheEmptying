using TheEmptying.Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>
    /// One node's visual: a header colored by block kind (or a clearly marked
    /// missing-block color), the display name, a colored dot per port
    /// (execution ports square, data ports round and colored by type), and
    /// read-only field value rows. Purely a drawing of a NodeViewModel - it
    /// holds no logic of its own.
    /// </summary>
    internal sealed class NodeElement : VisualElement
    {
        private const float PortDotSize = 10f;

        private readonly VisualElement _header;
        private readonly Label _title;
        private readonly VisualElement _portsAndFields;

        public string NodeId { get; }

        public NodeElement(NodeViewModel node)
        {
            NodeId = node.Node.Id;

            style.position = Position.Absolute;
            style.backgroundColor = new Color(0.16f, 0.16f, 0.18f, 0.97f);
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1f;
            SetBorderColor(new Color(0f, 0f, 0f, 0.6f));

            _header = new VisualElement
            {
                style =
                {
                    height = NodeViewModel.HeaderHeight,
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 6,
                    paddingRight = 6
                }
            };

            _title = new Label { style = { color = Color.white, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } };
            _header.Add(_title);
            Add(_header);

            _portsAndFields = new VisualElement { pickingMode = PickingMode.Ignore };
            Add(_portsAndFields);

            foreach (PortViewModel port in node.Ports)
            {
                _portsAndFields.Add(BuildPortLabel(port));
            }

            float fieldY = NodeViewModel.HeaderHeight + NodeViewModel.PortsTopPadding +
                           System.Math.Max(node.InputPorts.Count, node.OutputPorts.Count) * NodeViewModel.PortRowHeight;
            foreach ((string name, string displayValue) in node.FieldSummaries)
            {
                var fieldLabel = new Label($"{name}: {displayValue}")
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = 6,
                        top = fieldY,
                        width = NodeViewModel.Width - 12,
                        fontSize = 9,
                        color = new Color(0.75f, 0.75f, 0.75f),
                        overflow = Overflow.Hidden,
                        textOverflow = TextOverflow.Ellipsis,
                        whiteSpace = WhiteSpace.NoWrap
                    }
                };
                _portsAndFields.Add(fieldLabel);
                fieldY += NodeViewModel.FieldRowHeight;
            }

            // A port dot sits exactly on the node's edge, on top of everything else.
            foreach (PortViewModel port in node.Ports)
            {
                Add(BuildPortDot(port));
            }

            Refresh(node);
        }

        private static VisualElement BuildPortLabel(PortViewModel port)
        {
            bool isInput = port.Direction == PortDirection.Input;
            var label = new Label(port.Name)
            {
                style =
                {
                    position = Position.Absolute,
                    top = port.LocalOffset.y - 7,
                    fontSize = 9,
                    color = new Color(0.85f, 0.85f, 0.85f)
                }
            };

            if (isInput)
            {
                label.style.left = port.LocalOffset.x + 8;
            }
            else
            {
                label.style.right = NodeViewModel.Width - port.LocalOffset.x + 8;
            }

            return label;
        }

        private static VisualElement BuildPortDot(PortViewModel port)
        {
            const float borderWidth = 1.5f;

            var dot = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    width = PortDotSize,
                    height = PortDotSize,
                    left = port.LocalOffset.x - PortDotSize * 0.5f,
                    top = port.LocalOffset.y - PortDotSize * 0.5f,
                    backgroundColor = port.Kind == PortKind.Execution ? RuleCanvasColors.ExecutionWire : RuleCanvasColors.DataTypeColor(port.DataType),
                    borderTopWidth = borderWidth,
                    borderBottomWidth = borderWidth,
                    borderLeftWidth = borderWidth,
                    borderRightWidth = borderWidth
                }
            };

            // Execution ports draw as squares (sharp corners); data ports draw as rounded dots.
            if (port.Kind != PortKind.Execution)
            {
                dot.style.borderTopLeftRadius = PortDotSize * 0.5f;
                dot.style.borderTopRightRadius = PortDotSize * 0.5f;
                dot.style.borderBottomLeftRadius = PortDotSize * 0.5f;
                dot.style.borderBottomRightRadius = PortDotSize * 0.5f;
            }

            Color borderColor = port.IsInferred ? RuleCanvasColors.InferredPort : new Color(0f, 0f, 0f, 0.6f);
            dot.style.borderTopColor = borderColor;
            dot.style.borderBottomColor = borderColor;
            dot.style.borderLeftColor = borderColor;
            dot.style.borderRightColor = borderColor;

            return dot;
        }

        /// <summary>Repositions and recolors this element to match the node's current state (position, size, kind/missing header color, selection highlight). Called every frame the view refreshes, so it stays cheap.</summary>
        public void Refresh(NodeViewModel node)
        {
            style.left = node.Position.x;
            style.top = node.Position.y;
            style.width = node.Size.x;
            style.height = node.Size.y;

            _header.style.backgroundColor = node.IsMissing ? RuleCanvasColors.MissingHeader : RuleCanvasColors.HeaderColor(node.Kind ?? BlockKind.Action);
            _title.text = node.IsMissing ? $"Missing: {node.DisplayName}" : node.DisplayName;
        }

        public void SetSelected(bool selected)
        {
            SetBorderColor(selected ? RuleCanvasColors.SelectionHighlight : new Color(0f, 0f, 0f, 0.6f));
            SetBorderWidth(selected ? 2f : 1f);
        }

        private void SetBorderColor(Color color)
        {
            style.borderTopColor = color;
            style.borderBottomColor = color;
            style.borderLeftColor = color;
            style.borderRightColor = color;
        }

        private void SetBorderWidth(float width)
        {
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
        }
    }
}
