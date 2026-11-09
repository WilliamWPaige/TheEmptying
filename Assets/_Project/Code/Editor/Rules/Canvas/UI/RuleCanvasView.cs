using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Editor.Rules.Editing;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>
    /// The visible, editable canvas for one RuleEditorModel: draws whatever
    /// its RuleCanvasViewModel holds and turns pointer/wheel/key input into
    /// calls on the model. Holds only UI-local state that doesn't belong in
    /// undo history (which port a wire is being dragged from, which wire is
    /// selected, pan/box-select bookkeeping) - everything that matters is in
    /// the model.
    /// </summary>
    public sealed class RuleCanvasView : VisualElement
    {
        private const float PortHitRadiusScreen = 10f;
        private const float WireHitRadiusScreen = 6f;

        private readonly RuleEditorModel _model;
        private readonly RuleCanvasViewModel _viewModel;
        private readonly GridBackground _gridBackground;
        private readonly WiresLayer _wiresLayer;
        private readonly VisualElement _nodesContainer;
        private readonly VisualElement _selectionBoxElement;
        private readonly PendingWireLayer _pendingWireLayer;
        private readonly Dictionary<string, NodeElement> _nodeElements = new Dictionary<string, NodeElement>();

        private bool _isPanning;
        private bool _isDraggingNodes;
        private bool _isBoxSelecting;
        private bool _isDraggingWire;
        private (NodeViewModel Node, PortViewModel Port)? _wireDragFrom;
        private WireViewModel _selectedWire;
        private Vector2 _lastScreenPos;
        private Vector2 _dragLastCanvasPoint;
        private Vector2 _boxSelectStartScreen;

        /// <summary>Raised when the user asks for the add-node menu (right-click or Space on empty canvas, or releasing a dragged wire on empty canvas).</summary>
        public event Action<AddNodeMenuRequest> AddNodeMenuRequested;

        public RuleCanvasView(RuleEditorModel model)
        {
            _model = model;
            _viewModel = model.Canvas;

            style.flexGrow = 1;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new Color(0.14f, 0.14f, 0.15f);
            focusable = true;

            _gridBackground = new GridBackground(_viewModel);
            Add(_gridBackground);

            // Nodes live in a transform-scaled, transform-translated container
            // (origin at its own top-left) so every child position authored in
            // canvas units - header height, port offsets, field rows - scales
            // and pans together automatically; no manual re-scaling anywhere else.
            _nodesContainer = new VisualElement { pickingMode = PickingMode.Ignore };
            _nodesContainer.style.position = Position.Absolute;
            _nodesContainer.style.left = 0;
            _nodesContainer.style.top = 0;
            _nodesContainer.style.transformOrigin = new TransformOrigin(0, 0);
            Add(_nodesContainer);

            // Wires (and the in-progress drag preview) are hand-painted directly
            // in screen space (via CanvasToScreen), not inside the transformed
            // container, so they aren't bound by its box.
            _wiresLayer = new WiresLayer(_viewModel);
            Add(_wiresLayer);

            _pendingWireLayer = new PendingWireLayer(this);
            Add(_pendingWireLayer);

            _selectionBoxElement = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    display = DisplayStyle.None,
                    backgroundColor = RuleCanvasColors.SelectionBoxFill,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = RuleCanvasColors.SelectionBoxBorder,
                    borderBottomColor = RuleCanvasColors.SelectionBoxBorder,
                    borderLeftColor = RuleCanvasColors.SelectionBoxBorder,
                    borderRightColor = RuleCanvasColors.SelectionBoxBorder
                }
            };
            Add(_selectionBoxElement);

            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());

            Refresh();
        }

        /// <summary>Call after any change to the rule data, the model, or this element's size - repositions every node element, repaints wires and the grid, and reflects the current selection and validation state.</summary>
        public void Refresh()
        {
            SyncNodeElements();
            ApplyTransform();
            ApplyHighlight();
            _gridBackground.Refresh();
            _wiresLayer.Refresh();
            _pendingWireLayer.Refresh();
        }

        private RuleTraceSegment _highlightedSegment;

        /// <summary>Marks every node a trace segment's run touched: execution order, followed wires, the values read/computed at each port, and an error label on whichever node it ended on.</summary>
        public void HighlightSegment(RuleTraceSegment segment)
        {
            _highlightedSegment = segment;
            ApplyHighlight();
        }

        public void ClearHighlight()
        {
            _highlightedSegment = null;
            ApplyHighlight();
        }

        private void ApplyHighlight()
        {
            if (_highlightedSegment == null)
            {
                foreach (NodeElement element in _nodeElements.Values)
                {
                    element.SetExecutionOrder(null);
                    element.SetRuntimeError(null);
                    element.ClearPortValues();
                }

                _wiresLayer.SetHighlightedFromPorts(null);
                return;
            }

            RuleHighlightData data = RuleHighlightData.Build(_highlightedSegment);

            foreach (NodeViewModel node in _viewModel.Nodes)
            {
                if (!_nodeElements.TryGetValue(node.Node.Id, out NodeElement element))
                {
                    continue;
                }

                element.SetExecutionOrder(data.ExecutionOrderByNode.TryGetValue(node.Node.Id, out int ord) ? ord : (int?)null);
                element.SetRuntimeError(data.ErrorNodeId == node.Node.Id ? data.ErrorMessage : null);
                element.ClearPortValues();

                if (data.InputValuesByNode.TryGetValue(node.Node.Id, out List<(string Port, string Value)> inputs))
                {
                    foreach ((string port, string value) in inputs)
                    {
                        element.SetPortValue(port, PortDirection.Input, value);
                    }
                }

                if (data.ComputedValueByNode.TryGetValue(node.Node.Id, out string outputValue))
                {
                    PortViewModel outputPort = node.OutputPorts.FirstOrDefault();
                    if (outputPort != null)
                    {
                        element.SetPortValue(outputPort.Name, PortDirection.Output, outputValue);
                    }
                }
            }

            _wiresLayer.SetHighlightedFromPorts(data.FollowedExecutionOutputs);
        }

        private void ApplyTransform()
        {
            _nodesContainer.transform.position = new Vector3(_viewModel.Pan.x, _viewModel.Pan.y, 0f);
            _nodesContainer.transform.scale = new Vector3(_viewModel.Zoom, _viewModel.Zoom, 1f);
        }

        private void SyncNodeElements()
        {
            var seenIds = new HashSet<string>();
            IReadOnlyCollection<string> errorNodeIds = _model.NodeIdsWithErrors;
            IReadOnlyCollection<string> warningNodeIds = _model.NodeIdsWithWarnings;

            foreach (NodeViewModel node in _viewModel.Nodes)
            {
                seenIds.Add(node.Node.Id);

                if (!_nodeElements.TryGetValue(node.Node.Id, out NodeElement element))
                {
                    element = new NodeElement(node, _model);
                    _nodeElements[node.Node.Id] = element;
                    _nodesContainer.Add(element);
                }

                element.Refresh(node);

                NodeElement.ValidationState validation = errorNodeIds.Contains(node.Node.Id) ? NodeElement.ValidationState.Error
                    : warningNodeIds.Contains(node.Node.Id) ? NodeElement.ValidationState.Warning
                    : NodeElement.ValidationState.None;

                element.SetSelected(_viewModel.SelectedNodeIds.Contains(node.Node.Id));
                element.SetValidationState(validation);
            }

            List<string> stale = _nodeElements.Keys.Where(id => !seenIds.Contains(id)).ToList();
            foreach (string id in stale)
            {
                _nodesContainer.Remove(_nodeElements[id]);
                _nodeElements.Remove(id);
            }
        }

        /// <summary>Selects and frames a node (e.g. from the problems panel).</summary>
        public void SelectAndFrameNode(string nodeId)
        {
            _viewModel.SelectNodeById(nodeId, additive: false);
            _viewModel.FrameSelected(layout.size);
            Refresh();
        }

        // --- Input ---

        private void OnWheel(WheelEvent evt)
        {
            float factor = evt.delta.y > 0f ? 1f / 1.1f : 1.1f;
            _viewModel.ZoomByFactor(evt.localMousePosition, factor);
            Refresh();
            evt.StopPropagation();
        }

        // Unity's UI Toolkit pointer events number mouse buttons left=0, right=1,
        // middle=2 (not the W3C/browser convention of left=0, middle=1, right=2).
        public const int LeftMouseButton = 0;
        public const int RightMouseButton = 1;
        public const int MiddleMouseButton = 2;

        /// <summary>Panning is middle-drag or Alt+left-drag only. Right-click is reserved for the context (add-node) menu. Pure and tested at the input-forwarding level without needing a live panel.</summary>
        public static bool IsPanTrigger(int button, bool altKey) => button == MiddleMouseButton || (button == LeftMouseButton && altKey);

        private float PortHitRadiusCanvas => PortHitRadiusScreen / _viewModel.Zoom;
        private float WireHitRadiusCanvas => WireHitRadiusScreen / _viewModel.Zoom;

        private void OnPointerDown(PointerDownEvent evt)
        {
            Focus();
            _lastScreenPos = evt.localPosition;

            if (IsPanTrigger(evt.button, evt.altKey))
            {
                this.CapturePointer(evt.pointerId);
                _isPanning = true;
                return;
            }

            Vector2 canvasPoint = _viewModel.ScreenToCanvas(evt.localPosition);

            if (evt.button == RightMouseButton)
            {
                if (_viewModel.HitTestNode(canvasPoint) == null && _viewModel.HitTestPort(canvasPoint, PortHitRadiusCanvas) == null)
                {
                    AddNodeMenuRequested?.Invoke(new AddNodeMenuRequest(evt.localPosition, canvasPoint, null, null));
                }

                return;
            }

            if (evt.button != LeftMouseButton)
            {
                return;
            }

            this.CapturePointer(evt.pointerId);

            (NodeViewModel Node, PortViewModel Port)? portHit = _viewModel.HitTestPort(canvasPoint, PortHitRadiusCanvas);
            if (portHit != null)
            {
                if (evt.altKey)
                {
                    _model.DisconnectAllAtPort(portHit.Value.Node.Node.Id, portHit.Value.Port.Name, portHit.Value.Port.Direction);
                    Refresh();
                    return;
                }

                _isDraggingWire = true;
                _wireDragFrom = portHit;
                _selectedWire = null;
                Refresh();
                return;
            }

            WireViewModel wireHit = _viewModel.HitTestWire(canvasPoint, WireHitRadiusCanvas);
            if (wireHit != null)
            {
                _selectedWire = wireHit;
                _viewModel.ClearSelection();
                Refresh();
                return;
            }

            _selectedWire = null;
            bool additive = evt.shiftKey || evt.ctrlKey;
            NodeViewModel hit = _viewModel.HitTestNode(canvasPoint);

            if (hit != null)
            {
                bool alreadySelected = _viewModel.SelectedNodeIds.Contains(hit.Node.Id);
                if (additive || !alreadySelected)
                {
                    _viewModel.SelectAt(canvasPoint, additive);
                }

                _isDraggingNodes = true;
                _dragLastCanvasPoint = canvasPoint;
                _model.BeginCombinedEdit();
            }
            else
            {
                _viewModel.SelectAt(canvasPoint, additive);
                _isBoxSelecting = true;
                _boxSelectStartScreen = evt.localPosition;
                _selectionBoxElement.style.display = DisplayStyle.Flex;
                UpdateSelectionBoxElement(_boxSelectStartScreen, _boxSelectStartScreen);
            }

            Refresh();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 screenPos = evt.localPosition;
            Vector2 screenDelta = screenPos - _lastScreenPos;
            _lastScreenPos = screenPos;

            if (_isPanning)
            {
                _viewModel.PanBy(screenDelta);
                Refresh();
                return;
            }

            if (_isDraggingWire)
            {
                Refresh();
                return;
            }

            if (_isDraggingNodes)
            {
                Vector2 canvasPoint = _viewModel.ScreenToCanvas(screenPos);
                _model.DragSelected(canvasPoint - _dragLastCanvasPoint);
                _dragLastCanvasPoint = canvasPoint;
                Refresh();
                return;
            }

            if (_isBoxSelecting)
            {
                UpdateSelectionBoxElement(_boxSelectStartScreen, screenPos);

                Vector2 c1 = _viewModel.ScreenToCanvas(_boxSelectStartScreen);
                Vector2 c2 = _viewModel.ScreenToCanvas(screenPos);
                Rect canvasRect = Rect.MinMaxRect(Mathf.Min(c1.x, c2.x), Mathf.Min(c1.y, c2.y), Mathf.Max(c1.x, c2.x), Mathf.Max(c1.y, c2.y));
                _viewModel.SelectInBox(canvasRect, additive: false);
                Refresh();
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            _isPanning = false;

            if (_isDraggingNodes)
            {
                _isDraggingNodes = false;
                _model.EndCombinedEdit();
            }

            if (_isBoxSelecting)
            {
                _isBoxSelecting = false;
                _selectionBoxElement.style.display = DisplayStyle.None;
            }

            if (_isDraggingWire)
            {
                _isDraggingWire = false;
                EndWireDrag(evt.localPosition);
            }

            if (this.HasPointerCapture(evt.pointerId))
            {
                this.ReleasePointer(evt.pointerId);
            }

            Refresh();
        }

        private void EndWireDrag(Vector2 screenPosition)
        {
            if (_wireDragFrom == null)
            {
                return;
            }

            (NodeViewModel Node, PortViewModel Port) source = _wireDragFrom.Value;
            _wireDragFrom = null;

            Vector2 canvasPoint = _viewModel.ScreenToCanvas(screenPosition);
            (NodeViewModel Node, PortViewModel Port)? targetHit = _viewModel.HitTestPort(canvasPoint, PortHitRadiusCanvas);

            if (targetHit != null && targetHit.Value.Node != source.Node)
            {
                (NodeViewModel Node, PortViewModel Port) target = targetHit.Value;

                if (source.Port.Direction == PortDirection.Output && target.Port.Direction == PortDirection.Input)
                {
                    _model.Connect(source.Node.Node.Id, source.Port.Name, target.Node.Node.Id, target.Port.Name);
                }
                else if (source.Port.Direction == PortDirection.Input && target.Port.Direction == PortDirection.Output)
                {
                    _model.Connect(target.Node.Node.Id, target.Port.Name, source.Node.Node.Id, source.Port.Name);
                }

                return;
            }

            if (_viewModel.HitTestNode(canvasPoint) == null)
            {
                AddNodeMenuRequested?.Invoke(new AddNodeMenuRequest(screenPosition, canvasPoint, source.Node, source.Port));
            }
        }

        private void UpdateSelectionBoxElement(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            _selectionBoxElement.style.left = x;
            _selectionBoxElement.style.top = y;
            _selectionBoxElement.style.width = Mathf.Abs(a.x - b.x);
            _selectionBoxElement.style.height = Mathf.Abs(a.y - b.y);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            bool ctrl = evt.ctrlKey || evt.commandKey;

            switch (evt.keyCode)
            {
                case KeyCode.F when evt.shiftKey:
                    _viewModel.FrameSelected(layout.size);
                    Refresh();
                    evt.StopPropagation();
                    return;

                case KeyCode.F:
                    _viewModel.FrameAll(layout.size);
                    Refresh();
                    evt.StopPropagation();
                    return;

                case KeyCode.Escape:
                    _viewModel.ClearSelection();
                    _selectedWire = null;
                    Refresh();
                    evt.StopPropagation();
                    return;

                case KeyCode.Space:
                    AddNodeMenuRequested?.Invoke(new AddNodeMenuRequest(_lastScreenPos, _viewModel.ScreenToCanvas(_lastScreenPos), null, null));
                    evt.StopPropagation();
                    return;

                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelection();
                    evt.StopPropagation();
                    return;
            }

            if (ctrl && evt.keyCode == KeyCode.Z && evt.shiftKey)
            {
                _model.Redo();
                Refresh();
                evt.StopPropagation();
                return;
            }

            if (ctrl && evt.keyCode == KeyCode.Z)
            {
                _model.Undo();
                Refresh();
                evt.StopPropagation();
                return;
            }

            if (ctrl && evt.keyCode == KeyCode.Y)
            {
                _model.Redo();
                Refresh();
                evt.StopPropagation();
                return;
            }

            if (ctrl && evt.keyCode == KeyCode.C)
            {
                if (_viewModel.SelectedNodeIds.Count > 0)
                {
                    GUIUtility.systemCopyBuffer = _model.CopySelectionToClipboardText();
                }

                evt.StopPropagation();
                return;
            }

            if (ctrl && evt.keyCode == KeyCode.V)
            {
                _model.PasteFromClipboardText(GUIUtility.systemCopyBuffer, _viewModel.ScreenToCanvas(_lastScreenPos));
                Refresh();
                evt.StopPropagation();
                return;
            }

            if (ctrl && evt.keyCode == KeyCode.D)
            {
                _model.DuplicateSelection();
                Refresh();
                evt.StopPropagation();
            }
        }

        private void DeleteSelection()
        {
            if (_selectedWire != null)
            {
                _model.Disconnect(_selectedWire.Connection);
                _selectedWire = null;
                Refresh();
                return;
            }

            if (_viewModel.SelectedNodeIds.Count > 0)
            {
                _model.DeleteNodes(_viewModel.SelectedNodeIds.ToList());
                Refresh();
            }
        }

        /// <summary>Draws the in-progress wire drag, if any, as a line from the source port to the current mouse position - screen space, same as WiresLayer.</summary>
        private sealed class PendingWireLayer : VisualElement
        {
            private readonly RuleCanvasView _owner;

            public PendingWireLayer(RuleCanvasView owner)
            {
                _owner = owner;
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
                if (!_owner._isDraggingWire || _owner._wireDragFrom == null)
                {
                    return;
                }

                (NodeViewModel node, PortViewModel port) = _owner._wireDragFrom.Value;
                Vector2 from = _owner._viewModel.CanvasToScreen(node.Position + port.LocalOffset);
                Vector2 to = _owner._lastScreenPos;

                Painter2D painter = mgc.painter2D;
                painter.strokeColor = port.Kind == PortKind.Execution ? RuleCanvasColors.ExecutionWire : RuleCanvasColors.DataTypeColor(port.DataType);
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(from);
                painter.LineTo(to);
                painter.Stroke();
            }

            public void Refresh() => MarkDirtyRepaint();
        }
    }
}
