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
    /// R12: the HUD "Zones" button's type picker, and the click-click-
    /// click rectangle-at-any-angle drawing tool: click to set the
    /// start, a line follows the mouse to set the first side's length
    /// and angle, click to lock it, move the mouse to pull out the
    /// second side, click to finish. Right click or Escape cancels at
    /// any point. Shows a live outline and a size label throughout.
    /// </summary>
    public sealed class ZoneDrawingPresenter : MonoBehaviour
    {
        private enum DrawState
        {
            None,
            AwaitingStart,
            AwaitingFirstSide,
            AwaitingSecondSide
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
        private double _sideALength;
        private double _rotationDegrees;

        private ZoneVisual _preview;
        private VisualElement _typePickerPanel;
        private VisualElement _typeButtonRow;
        private Label _sizeLabel;

        public bool IsOpen => (_typePickerPanel != null && _typePickerPanel.style.display == DisplayStyle.Flex) || _state != DrawState.None;

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
            CancelDrawing();
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

        /// <summary>Escape's entry point - closes the type picker, or cancels an in-progress drawing, whichever is open.</summary>
        public void HandleEscapePressed()
        {
            if (_typePickerPanel.style.display == DisplayStyle.Flex)
            {
                CloseTypePicker();
                return;
            }

            CancelDrawing();
        }

        private void CloseTypePicker()
        {
            _typePickerPanel.style.display = DisplayStyle.None;
        }

        private void BeginDrawing(string zoneTypeId)
        {
            CloseTypePicker();
            _zoneTypeId = zoneTypeId;
            _state = DrawState.AwaitingStart;
        }

        private void CancelDrawing()
        {
            _state = DrawState.None;
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
                CancelDrawing();
                return;
            }

            if (!TryGetGroundPoint(out double worldX, out double worldZ))
            {
                return;
            }

            bool clicked = Mouse.current.leftButton.wasPressedThisFrame && !UiInputUtility.IsPointerOverUi(_uiRoot, Mouse.current.position.ReadValue());

            switch (_state)
            {
                case DrawState.AwaitingStart:
                    if (clicked)
                    {
                        _startX = worldX;
                        _startZ = worldZ;
                        _state = DrawState.AwaitingFirstSide;
                    }

                    break;

                case DrawState.AwaitingFirstSide:
                    HandleAwaitingFirstSide(worldX, worldZ, clicked);
                    break;

                case DrawState.AwaitingSecondSide:
                    HandleAwaitingSecondSide(worldX, worldZ, clicked);
                    break;
            }
        }

        private void HandleAwaitingFirstSide(double worldX, double worldZ, bool clicked)
        {
            double dx = worldX - _startX;
            double dz = worldZ - _startZ;
            double length = Math.Sqrt(dx * dx + dz * dz);
            double rotation = length > 0.001 ? Math.Atan2(dz, dx) * 180.0 / Math.PI : 0.0;

            double midX = (_startX + worldX) * 0.5;
            double midZ = (_startZ + worldZ) * 0.5;
            ShowPreview(new OrientedRect(midX, midZ, length, 0, rotation));
            UpdateSizeLabel(length, 0);

            if (clicked && length >= 0.01)
            {
                _sideALength = length;
                _rotationDegrees = rotation;
                _state = DrawState.AwaitingSecondSide;
            }
        }

        private void HandleAwaitingSecondSide(double worldX, double worldZ, bool clicked)
        {
            double rad = _rotationDegrees * Math.PI / 180.0;
            double axisAx = Math.Cos(rad);
            double axisAz = Math.Sin(rad);
            double axisBx = -Math.Sin(rad);
            double axisBz = Math.Cos(rad);

            double dx = worldX - _startX;
            double dz = worldZ - _startZ;
            double signedSideB = dx * axisBx + dz * axisBz;

            double centerX = _startX + _sideALength * 0.5 * axisAx + signedSideB * 0.5 * axisBx;
            double centerZ = _startZ + _sideALength * 0.5 * axisAz + signedSideB * 0.5 * axisBz;

            var rect = new OrientedRect(centerX, centerZ, _sideALength, Math.Abs(signedSideB), _rotationDegrees);
            ShowPreview(rect);
            UpdateSizeLabel(_sideALength, signedSideB);

            if (clicked)
            {
                FinishDrawing(rect);
            }
        }

        private void FinishDrawing(OrientedRect rect)
        {
            string zoneTypeId = _zoneTypeId;
            CancelDrawing();

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

        private void ShowPreview(OrientedRect rect)
        {
            if (_preview == null)
            {
                _preview = new ZoneVisual(transform, _zonePresenter.ColorFor(_zoneTypeId), collidable: false);
            }

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
