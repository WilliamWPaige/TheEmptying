using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>
    /// The visible canvas: draws whatever RuleCanvasViewModel holds and
    /// forwards input to it. Holds no graph logic of its own - every
    /// position, selection and hit-test decision is made by the view-model;
    /// this only turns pointer/wheel/key events into calls on it and repaints.
    /// </summary>
    public sealed class RuleCanvasView : VisualElement
    {
        private readonly RuleCanvasViewModel _viewModel;
        private readonly GridBackground _gridBackground;
        private readonly WiresLayer _wiresLayer;
        private readonly VisualElement _nodesContainer;
        private readonly VisualElement _selectionBoxElement;
        private readonly Dictionary<string, NodeElement> _nodeElements = new Dictionary<string, NodeElement>();

        private bool _isPanning;
        private bool _isDraggingNodes;
        private bool _isBoxSelecting;
        private Vector2 _lastScreenPos;
        private Vector2 _dragLastCanvasPoint;
        private Vector2 _boxSelectStartScreen;

        public RuleCanvasView(RuleCanvasViewModel viewModel)
        {
            _viewModel = viewModel;

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

            // Wires are hand-painted directly in screen space (via CanvasToScreen),
            // not inside the transformed container, so they aren't bound by its box.
            _wiresLayer = new WiresLayer(_viewModel);
            Add(_wiresLayer);

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

        /// <summary>Call after any change to the rule data, the view-model, or this element's size - repositions every node element, repaints wires and the grid, and reflects the current selection.</summary>
        public void Refresh()
        {
            SyncNodeElements();
            ApplyTransform();
            _gridBackground.Refresh();
            _wiresLayer.Refresh();
        }

        private void ApplyTransform()
        {
            _nodesContainer.transform.position = new Vector3(_viewModel.Pan.x, _viewModel.Pan.y, 0f);
            _nodesContainer.transform.scale = new Vector3(_viewModel.Zoom, _viewModel.Zoom, 1f);
        }

        private void SyncNodeElements()
        {
            var seenIds = new HashSet<string>();

            foreach (NodeViewModel node in _viewModel.Nodes)
            {
                seenIds.Add(node.Node.Id);

                if (!_nodeElements.TryGetValue(node.Node.Id, out NodeElement element))
                {
                    element = new NodeElement(node);
                    _nodeElements[node.Node.Id] = element;
                    _nodesContainer.Add(element);
                }

                element.Refresh(node);
                element.SetSelected(_viewModel.SelectedNodeIds.Contains(node.Node.Id));
            }

            List<string> stale = _nodeElements.Keys.Where(id => !seenIds.Contains(id)).ToList();
            foreach (string id in stale)
            {
                _nodesContainer.Remove(_nodeElements[id]);
                _nodeElements.Remove(id);
            }
        }

        // --- Input ---

        private void OnWheel(WheelEvent evt)
        {
            float factor = evt.delta.y > 0f ? 1f / 1.1f : 1.1f;
            _viewModel.ZoomByFactor(evt.localMousePosition, factor);
            Refresh();
            evt.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            Focus();
            this.CapturePointer(evt.pointerId);
            _lastScreenPos = evt.localPosition;

            bool isPanTrigger = evt.button == 1 || (evt.button == 0 && evt.altKey);
            if (isPanTrigger)
            {
                _isPanning = true;
                return;
            }

            if (evt.button != 0)
            {
                return;
            }

            Vector2 canvasPoint = _viewModel.ScreenToCanvas(evt.localPosition);
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

            if (_isPanning)
            {
                _viewModel.PanBy(screenPos - _lastScreenPos);
                _lastScreenPos = screenPos;
                Refresh();
                return;
            }

            if (_isDraggingNodes)
            {
                Vector2 canvasPoint = _viewModel.ScreenToCanvas(screenPos);
                _viewModel.DragSelected(canvasPoint - _dragLastCanvasPoint);
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
            _isDraggingNodes = false;

            if (_isBoxSelecting)
            {
                _isBoxSelecting = false;
                _selectionBoxElement.style.display = DisplayStyle.None;
            }

            if (this.HasPointerCapture(evt.pointerId))
            {
                this.ReleasePointer(evt.pointerId);
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
            switch (evt.keyCode)
            {
                case KeyCode.F when evt.shiftKey:
                    _viewModel.FrameSelected(layout.size);
                    Refresh();
                    evt.StopPropagation();
                    break;

                case KeyCode.F:
                    _viewModel.FrameAll(layout.size);
                    Refresh();
                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    _viewModel.ClearSelection();
                    Refresh();
                    evt.StopPropagation();
                    break;
            }
        }
    }
}
