using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Editing;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>
    /// One node's visual: a header colored by block kind (or a clearly marked
    /// missing-block color), the display name, a colored dot per port
    /// (execution ports square, data ports round and colored by type), and -
    /// for a resolved block type - editable field controls (a family block
    /// also gets a type dropdown that swaps variants). A field whose port is
    /// wired is shown greyed out. Builds its own controls once; Refresh only
    /// repositions/recolors and re-reads values (so undo/redo shows up
    /// without disturbing whatever the user is actively typing into).
    /// </summary>
    internal sealed class NodeElement : VisualElement
    {
        public enum ValidationState
        {
            None,
            Warning,
            Error
        }

        private const float PortDotSize = 10f;
        private static readonly Color DefaultBorderColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color WarningBorderColor = new Color(0.9f, 0.8f, 0.2f);
        private static readonly Color ErrorBorderColor = new Color(0.9f, 0.25f, 0.25f);

        private readonly RuleEditorModel _model;
        private readonly VisualElement _header;
        private readonly Label _title;
        private readonly VisualElement _portsAndFields;
        private readonly List<Action<RuleNode>> _fieldRefreshers = new List<Action<RuleNode>>();

        private bool _isSelected;
        private ValidationState _validationState = ValidationState.None;

        public string NodeId { get; }

        public NodeElement(NodeViewModel node, RuleEditorModel model)
        {
            _model = model;
            NodeId = node.Node.Id;

            style.position = Position.Absolute;
            style.backgroundColor = new Color(0.16f, 0.16f, 0.18f, 0.97f);
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1f;
            SetBorderColor(DefaultBorderColor);

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
                           Math.Max(node.InputPorts.Count, node.OutputPorts.Count) * NodeViewModel.PortRowHeight;

            if (BlockFamilyCatalog.TryGetFamilyForBlockType(BlockFamilyCatalog.DetectFamilies(_model.Context.Blocks), node.Node.BlockTypeId, out BlockFamily family))
            {
                _portsAndFields.Add(BuildVariantDropdown(family, node.Node.BlockTypeId, fieldY));
                fieldY += NodeViewModel.FieldRowHeight + 2f;
            }

            if (!node.IsMissing)
            {
                foreach (FieldDefinition field in node.BlockType.Fields)
                {
                    _portsAndFields.Add(BuildFieldRow(field, node, fieldY));
                    fieldY += NodeViewModel.FieldRowHeight + 2f;
                }
            }
            else
            {
                foreach ((string name, string displayValue) in node.FieldSummaries)
                {
                    _portsAndFields.Add(BuildReadOnlyLabel($"{name}: {displayValue}", fieldY));
                    fieldY += NodeViewModel.FieldRowHeight;
                }
            }

            // A port dot sits exactly on the node's edge, on top of everything else.
            foreach (PortViewModel port in node.Ports)
            {
                Add(BuildPortDot(port));
            }

            Refresh(node);
        }

        private static Label BuildReadOnlyLabel(string text, float y)
        {
            return new Label(text)
            {
                style =
                {
                    position = Position.Absolute,
                    left = 6,
                    top = y,
                    width = NodeViewModel.Width - 12,
                    fontSize = 9,
                    color = new Color(0.75f, 0.75f, 0.75f),
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    whiteSpace = WhiteSpace.NoWrap
                }
            };
        }

        private VisualElement BuildVariantDropdown(BlockFamily family, string currentBlockTypeId, float y)
        {
            List<RuleDataType> types = family.VariantsByType.Keys.OrderBy(t => t.ToString(), StringComparer.Ordinal).ToList();
            List<string> choices = types.Select(t => t.ToString()).ToList();
            RuleDataType currentType = family.VariantsByType.First(kvp => kvp.Value.Id == currentBlockTypeId).Key;

            var dropdown = new DropdownField(choices, choices.IndexOf(currentType.ToString()))
            {
                style = { position = Position.Absolute, left = 4, top = y, width = NodeViewModel.Width - 8, fontSize = 9, height = NodeViewModel.FieldRowHeight + 4 }
            };

            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (Enum.TryParse(evt.newValue, out RuleDataType newType) && family.TryGetVariant(newType, out BlockType newBlockType))
                {
                    _model.SwapVariant(NodeId, newBlockType.Id);
                }
            });

            return dropdown;
        }

        private VisualElement BuildFieldRow(FieldDefinition field, NodeViewModel node, float y)
        {
            var row = new VisualElement
            {
                style = { position = Position.Absolute, left = 4, top = y, width = NodeViewModel.Width - 8, height = NodeViewModel.FieldRowHeight + 4, flexDirection = FlexDirection.Row }
            };

            var label = new Label(field.Name) { style = { fontSize = 9, color = new Color(0.7f, 0.7f, 0.7f), width = 44, overflow = Overflow.Hidden, unityTextAlign = TextAnchor.MiddleLeft } };
            row.Add(label);

            VisualElement typedControl = BuildTypedControl(field);
            typedControl.style.flexGrow = 1;
            typedControl.style.height = NodeViewModel.FieldRowHeight + 2;

            DropdownField settingPicker = null;
            Button modeToggle = null;

            if (field.AllowsSettingReference)
            {
                List<string> compatibleSettingIds = _model.Context.Settings.ExportAll()
                    .Where(s => IsCompatibleSettingType(field.DataType, s.ValueType))
                    .Select(s => s.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();

                var dropdown = new DropdownField(NonEmpty(compatibleSettingIds), 0) { style = { flexGrow = 1, height = NodeViewModel.FieldRowHeight + 2, display = DisplayStyle.None } };
                settingPicker = dropdown;
                row.Add(dropdown);

                modeToggle = new Button { text = "S", style = { width = 16, height = NodeViewModel.FieldRowHeight + 2, fontSize = 8 } };
                modeToggle.tooltip = "Switch between a typed value and a named setting.";
                row.Add(modeToggle);
            }

            row.Insert(1, typedControl);

            void Commit(RuleFieldValueSource value)
            {
                _model.SetFieldValue(NodeId, field.Name, value);
            }

            WireTypedControlEvents(typedControl, field, Commit);

            if (settingPicker != null)
            {
                settingPicker.RegisterValueChangedCallback(evt =>
                {
                    _model.BeginCombinedEdit();
                    Commit(RuleFieldValueSource.FromSetting(evt.newValue));
                    _model.EndCombinedEdit();
                });

                modeToggle.clicked += () =>
                {
                    bool goingToSetting = typedControl.style.display == DisplayStyle.Flex;
                    typedControl.style.display = goingToSetting ? DisplayStyle.None : DisplayStyle.Flex;
                    settingPicker.style.display = goingToSetting ? DisplayStyle.Flex : DisplayStyle.None;

                    if (goingToSetting && settingPicker.choices.Count > 0)
                    {
                        _model.BeginCombinedEdit();
                        Commit(RuleFieldValueSource.FromSetting(settingPicker.choices[0]));
                        _model.EndCombinedEdit();
                    }
                };
            }

            _fieldRefreshers.Add(ruleNode =>
            {
                ruleNode.Fields.TryGetValue(field.Name, out RuleFieldValueSource source);
                bool isWired = _model.Rule.Connections.Any(c => c.ToNode == NodeId && c.ToPort == field.Name);
                row.SetEnabled(!isWired);
                row.style.opacity = isWired ? 0.5f : 1f;

                bool isSettingRef = source != null && source.IsSettingReference;
                typedControl.style.display = isSettingRef ? DisplayStyle.None : DisplayStyle.Flex;
                if (settingPicker != null)
                {
                    settingPicker.style.display = isSettingRef ? DisplayStyle.Flex : DisplayStyle.None;
                    if (isSettingRef)
                    {
                        settingPicker.SetValueWithoutNotify(source.SettingId);
                    }
                }

                if (!isSettingRef)
                {
                    SetTypedControlValueWithoutNotify(typedControl, field, source);
                }
            });

            return row;
        }

        private static bool IsCompatibleSettingType(RuleDataType fieldType, SettingValueType settingType)
        {
            return fieldType switch
            {
                RuleDataType.Number => settingType == SettingValueType.Number || settingType == SettingValueType.Integer,
                RuleDataType.Bool => settingType == SettingValueType.Boolean,
                _ => false
            };
        }

        private VisualElement BuildTypedControl(FieldDefinition field)
        {
            if (field.AllowedValues != null && field.AllowedValues.Count > 0)
            {
                return new DropdownField(NonEmpty(field.AllowedValues.ToList()), 0);
            }

            switch (field.PickerKind)
            {
                case FieldPickerKind.EventId:
                    return new DropdownField(NonEmpty(_model.Context.Events.All.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToList()), 0);
                case FieldPickerKind.SettingId:
                    return new DropdownField(NonEmpty(_model.Context.Settings.ExportAll().Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal).ToList()), 0);
                case FieldPickerKind.ContentId:
                    List<string> ids = field.ContentTypeName != null && _model.Context.ContentIdsByType.TryGetValue(field.ContentTypeName, out IReadOnlyList<string> known)
                        ? known.ToList()
                        : new List<string>();
                    return new DropdownField(NonEmpty(ids), 0);
            }

            return field.DataType switch
            {
                RuleDataType.Number => new DoubleField(),
                RuleDataType.Bool => new Toggle(),
                _ => new TextField()
            };
        }

        private static List<string> NonEmpty(List<string> values) => values.Count > 0 ? values : new List<string> { string.Empty };

        private void WireTypedControlEvents(VisualElement control, FieldDefinition field, Action<RuleFieldValueSource> commit)
        {
            switch (control)
            {
                case TextField textField:
                    textField.RegisterCallback<FocusInEvent>(_ => _model.BeginCombinedEdit());
                    textField.RegisterValueChangedCallback(evt => commit(RuleFieldValueSource.FromLiteral(JToken.FromObject(evt.newValue))));
                    textField.RegisterCallback<FocusOutEvent>(_ => _model.EndCombinedEdit());
                    break;

                case DoubleField doubleField:
                    doubleField.RegisterCallback<FocusInEvent>(_ => _model.BeginCombinedEdit());
                    doubleField.RegisterValueChangedCallback(evt => commit(RuleFieldValueSource.FromLiteral(JToken.FromObject(evt.newValue))));
                    doubleField.RegisterCallback<FocusOutEvent>(_ => _model.EndCombinedEdit());
                    break;

                case Toggle toggle:
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        _model.BeginCombinedEdit();
                        commit(RuleFieldValueSource.FromLiteral(JToken.FromObject(evt.newValue)));
                        _model.EndCombinedEdit();
                    });
                    break;

                case DropdownField dropdown:
                    dropdown.RegisterValueChangedCallback(evt =>
                    {
                        _model.BeginCombinedEdit();
                        commit(RuleFieldValueSource.FromLiteral(JToken.FromObject(evt.newValue)));
                        _model.EndCombinedEdit();
                    });
                    break;
            }
        }

        private static void SetTypedControlValueWithoutNotify(VisualElement control, FieldDefinition field, RuleFieldValueSource source)
        {
            switch (control)
            {
                case TextField textField:
                    textField.SetValueWithoutNotify(ResolveText(field, source));
                    break;
                case DoubleField doubleField:
                    doubleField.SetValueWithoutNotify(ResolveNumber(field, source));
                    break;
                case Toggle toggle:
                    toggle.SetValueWithoutNotify(ResolveBool(field, source));
                    break;
                case DropdownField dropdown:
                    dropdown.SetValueWithoutNotify(ResolveText(field, source));
                    break;
            }
        }

        private static string ResolveText(FieldDefinition field, RuleFieldValueSource source)
        {
            if (source == null)
            {
                return field.DefaultValue as string ?? string.Empty;
            }

            return source.Literal == null || source.Literal.Type == JTokenType.Null ? string.Empty : source.Literal.Value<string>();
        }

        private static double ResolveNumber(FieldDefinition field, RuleFieldValueSource source)
        {
            if (source == null)
            {
                return field.DefaultValue is double d ? d : 0.0;
            }

            return source.Literal == null || source.Literal.Type == JTokenType.Null ? 0.0 : source.Literal.Value<double>();
        }

        private static bool ResolveBool(FieldDefinition field, RuleFieldValueSource source)
        {
            if (source == null)
            {
                return field.DefaultValue is bool b && b;
            }

            return source.Literal != null && source.Literal.Type != JTokenType.Null && source.Literal.Value<bool>();
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

        /// <summary>Repositions and recolors this element to match the node's current state (position, size, kind/missing header color, selection/validation highlight), and refreshes every field control's displayed value - cheap, safe to call every frame the view refreshes.</summary>
        public void Refresh(NodeViewModel node)
        {
            style.left = node.Position.x;
            style.top = node.Position.y;
            style.width = node.Size.x;
            style.height = node.Size.y;

            _header.style.backgroundColor = node.IsMissing ? RuleCanvasColors.MissingHeader : RuleCanvasColors.HeaderColor(node.Kind ?? BlockKind.Action);
            _title.text = node.IsMissing ? $"Missing: {node.DisplayName}" : node.DisplayName;

            foreach (Action<RuleNode> refresher in _fieldRefreshers)
            {
                refresher(node.Node);
            }

            ApplyBorder();
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            ApplyBorder();
        }

        public void SetValidationState(ValidationState state)
        {
            _validationState = state;
            ApplyBorder();
        }

        private void ApplyBorder()
        {
            Color color = _isSelected ? RuleCanvasColors.SelectionHighlight
                : _validationState == ValidationState.Error ? ErrorBorderColor
                : _validationState == ValidationState.Warning ? WarningBorderColor
                : DefaultBorderColor;

            float width = _isSelected || _validationState != ValidationState.None ? 2f : 1f;

            SetBorderColor(color);
            SetBorderWidth(width);
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
