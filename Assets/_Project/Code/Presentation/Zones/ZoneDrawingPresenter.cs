using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R12b: the HUD "Zones" button's type picker, and the press-drag-
    /// release drawing tool: press the left button on the ground to
    /// start, drag, release to create. The box is always a clean,
    /// straight box lined up with the camera's current facing (so
    /// turning the camera with Q/E re-aligns it live) - see
    /// ZoneDrawingMath. Invalid (too small, or overlapping the keep or
    /// another zone) shows red and releasing does nothing. After
    /// placing one zone, the tool stays armed with the same type;
    /// right click or Escape while not dragging exits it, and while
    /// dragging just cancels that drag.
    /// </summary>
    public sealed class ZoneDrawingPresenter : MonoBehaviour
    {
        private enum DrawState
        {
            None,
            Armed,
            Dragging
        }

        private FloatingOrigin _floatingOrigin;
        private Camera _camera;
        private VisualElement _uiRoot;
        private ZonesModule _zonesModule;
        private ZonePresenter _zonePresenter;
        private ContentDatabase _content;
        private Action<string> _onMessage;
        private Action _onZonePlaced;

        private DrawState _state = DrawState.None;
        private string _zoneTypeId;
        private double _startX;
        private double _startZ;

        private ZoneVisual _preview;
        private VisualElement _typePickerPanel;
        private VisualElement _typeButtonRow;
        private Label _sizeLabel;

        private static readonly Color InvalidColor = new Color(0.85f, 0.15f, 0.15f);

        /// <summary>For the Escape chain's priority order: the type picker is showing, or a drag/armed tool is active.</summary>
        public bool IsOpen => (_typePickerPanel != null && _typePickerPanel.style.display == DisplayStyle.Flex) || _state != DrawState.None;

        /// <summary>R12b: true whenever the tool would consume a world click (armed or dragging) - the world click router must ignore clicks while this is true.</summary>
        public bool IsToolActive => _state != DrawState.None;

        public void Initialize(
            FloatingOrigin floatingOrigin,
            Camera camera,
            VisualElement uiRoot,
            ZonesModule zonesModule,
            ZonePresenter zonePresenter,
            ContentDatabase content,
            Action<string> onMessage,
            Action onZonePlaced)
        {
            _floatingOrigin = floatingOrigin;
            _camera = camera;
            _uiRoot = uiRoot;
            _zonesModule = zonesModule;
            _zonePresenter = zonePresenter;
            _content = content;
            _onMessage = onMessage;
            _onZonePlaced = onZonePlaced;

            BuildSizeLabel();
            BuildTypePicker();
        }

        /// <summary>After a load, the simulation (and its ZonesModule/content) is a new instance; any in-progress drawing is abandoned.</summary>
        public void Rebind(ZonesModule zonesModule, ContentDatabase content)
        {
            _zonesModule = zonesModule;
            _content = content;
            ExitTool();
            CloseTypePicker();
        }

        public void OpenTypePicker()
        {
            if (_state != DrawState.None)
            {
                return;
            }

            RefreshTypeButtons();
            _typePickerPanel.style.display = DisplayStyle.Flex;
        }

        /// <summary>Escape's entry point - closes the type picker, cancels a drag, or exits an armed (not dragging) tool, whichever applies.</summary>
        public void HandleEscapePressed()
        {
            if (_typePickerPanel.style.display == DisplayStyle.Flex)
            {
                CloseTypePicker();
                return;
            }

            if (_state == DrawState.Dragging)
            {
                CancelDrag();
                return;
            }

            ExitTool();
        }

        private void CloseTypePicker()
        {
            _typePickerPanel.style.display = DisplayStyle.None;
        }

        private void BeginDrawing(string zoneTypeId)
        {
            CloseTypePicker();
            _zoneTypeId = zoneTypeId;
            _state = DrawState.Armed;
        }

        private void ExitTool()
        {
            _state = DrawState.None;
            HidePreview();
        }

        /// <summary>Drops the current drag but keeps the tool armed with the same type, ready to draw another.</summary>
        private void CancelDrag()
        {
            _state = DrawState.Armed;
            HidePreview();
        }

        private void HidePreview()
        {
            _preview?.SetActive(false);
            if (_sizeLabel != null)
            {
                _sizeLabel.style.display = DisplayStyle.None;
            }
        }

        private void Update()
        {
            if (_typePickerPanel.style.display == DisplayStyle.Flex || _state == DrawState.None)
            {
                return;
            }

            if (Mouse.current == null)
            {
                return;
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                if (_state == DrawState.Dragging)
                {
                    CancelDrag();
                }
                else
                {
                    ExitTool();
                }

                return;
            }

            if (_state == DrawState.Armed)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame
                    && !UiInputUtility.IsPointerOverUi(_uiRoot, Mouse.current.position.ReadValue())
                    && TryGetGroundPoint(out double pressX, out double pressZ))
                {
                    _startX = pressX;
                    _startZ = pressZ;
                    _state = DrawState.Dragging;
                }

                return;
            }

            // Dragging.
            if (!TryGetGroundPoint(out double currentX, out double currentZ))
            {
                return;
            }

            double cameraYawDegrees = _camera.transform.eulerAngles.y;
            OrientedRect rect = ZoneDrawingMath.BuildRect(_startX, _startZ, currentX, currentZ, cameraYawDegrees);
            bool valid = _zonesModule.CanPlace(rect, out string invalidReason);

            ShowPreview(rect, valid);
            UpdateSizeLabel(rect.SideALength, rect.SideBLength);

            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                FinishDrag(rect, valid, invalidReason);
            }
        }

        private void FinishDrag(OrientedRect rect, bool valid, string invalidReason)
        {
            string zoneTypeId = _zoneTypeId;
            CancelDrag();

            if (!valid)
            {
                _onMessage?.Invoke(invalidReason ?? "Couldn't place that zone.");
                return;
            }

            EntityId? id = _zonesModule.TryPlaceZone(zoneTypeId, rect, out string reason);
            if (id == null)
            {
                _onMessage?.Invoke(reason ?? "Couldn't place that zone.");
                return;
            }

            _onMessage?.Invoke("Zone placed.");
            _onZonePlaced?.Invoke();
        }

        private bool TryGetGroundPoint(out double worldX, out double worldZ)
        {
            worldX = 0;
            worldZ = 0;

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            Ray ray = _camera.ScreenPointToRay(mousePosition);
            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float distance))
            {
                return false;
            }

            Vector3 worldHit = _floatingOrigin.RenderToWorld(ray.GetPoint(distance));
            worldX = worldHit.x;
            worldZ = worldHit.z;
            return true;
        }

        private void ShowPreview(OrientedRect rect, bool valid)
        {
            if (_preview == null)
            {
                _preview = new ZoneVisual(transform, _zonePresenter.ColorFor(_zoneTypeId), collidable: false);
            }

            _preview.SetColor(valid ? _zonePresenter.ColorFor(_zoneTypeId) : InvalidColor);
            _preview.UpdateGeometry(rect, _floatingOrigin);
            _preview.SetActive(true);
        }

        private void UpdateSizeLabel(double sideA, double sideB)
        {
            double absA = Math.Abs(sideA);
            double absB = Math.Abs(sideB);
            string text = $"{absA:0.#} x {absB:0.#} m";

            if (_zoneTypeId == ZonesModule.StockpileTypeId)
            {
                int slots = ZonesModule.SlotCount(absA * absB);
                text += $" ({slots} slot{(slots == 1 ? "" : "s")})";
            }

            _sizeLabel.text = text;
            _sizeLabel.style.display = DisplayStyle.Flex;
        }

        private void BuildSizeLabel()
        {
            _sizeLabel = new Label
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 56,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = Color.white,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f),
                    paddingTop = 4,
                    paddingBottom = 4,
                    display = DisplayStyle.None
                }
            };

            _uiRoot.Add(_sizeLabel);
        }

        private void BuildTypePicker()
        {
            _typePickerPanel = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    display = DisplayStyle.None
                }
            };

            var panel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Center,
                    paddingLeft = 24,
                    paddingRight = 24,
                    paddingTop = 16,
                    paddingBottom = 16,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f)
                }
            };

            panel.Add(new Label("Draw which kind of zone?") { style = { color = Color.white, marginBottom = 10 } });

            _typeButtonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 10 } };
            panel.Add(_typeButtonRow);

            panel.Add(new Button(CloseTypePicker) { text = "Cancel", style = { width = 120, height = 32 } });

            _typePickerPanel.Add(panel);
            _uiRoot.Add(_typePickerPanel);
        }

        private void RefreshTypeButtons()
        {
            _typeButtonRow.Clear();

            List<ZoneTypeDefinition> zoneTypes = BuiltInContentTypes.ZoneTypes(_content);
            foreach (ZoneTypeDefinition zoneType in zoneTypes)
            {
                string id = zoneType.Id;
                _typeButtonRow.Add(new Button(() => BeginDrawing(id)) { text = zoneType.DisplayName, style = { width = 120, height = 32, marginRight = 6 } });
            }
        }
    }
}
