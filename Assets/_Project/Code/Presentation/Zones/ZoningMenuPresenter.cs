using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation.DevTools;
using TheEmptying.Presentation.Discovery;
using TheEmptying.Presentation.People;
using TheEmptying.Presentation.Selection;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18: the zoning menu and its top-down view. Clicking Zones pauses
    /// the game, hides every other window, switches the camera to the
    /// orthographic zoning view, and shows this menu (Type, Name, Color,
    /// brush shape/size). "Start zoning" swaps the menu for a brush
    /// preview that follows the mouse, snapped to cells - painting
    /// itself, undo, Create and overlays all come in R18b. Leaving
    /// (Close or Escape) restores the UI, the camera exactly as it was,
    /// and the game speed from before.
    /// </summary>
    public sealed class ZoningMenuPresenter : MonoBehaviour
    {
        public const string DefaultNameSuffix = " Zone";
        public const string BrushMinSizeCellsSettingId = "setting.zoning.brush_min_size_cells";
        public const string BrushMaxSizeCellsSettingId = "setting.zoning.brush_max_size_cells";

        private StrategyCameraController _cameraController;
        private MapModule _mapModule;
        private ContentDatabase _content;
        private SettingsRegistry _settings;
        private FloatingOrigin _floatingOrigin;
        private Camera _camera;
        private IPresentationCommands _presentation;
        private VisualElement _uiRoot;
        private VisualElement _hudPanel;
        private TemporaryChunkDecorations _decorations;
        private PersonPresenter _personPresenter;
        private DebugWindowPresenter _debugWindow;
        private DiscoveriesWindowPresenter _discoveriesWindow;
        private WorldSelectionPresenter _worldSelection;
        private Action _hideToast;

        private VisualElement _root;
        private VisualElement _menuPanel;
        private DropdownField _typeField;
        private TextField _nameField;
        private ColorWheelPicker _colorPicker;
        private RadioButtonGroup _shapeField;
        private SliderInt _sizeField;

        private List<ZoneTypeDefinition> _zoneTypes = new List<ZoneTypeDefinition>();
        private bool _nameIsDefault = true;
        private bool _colorIsDefault = true;
        private bool _pausedBeforeOpen;
        private bool _armed;

        private BrushPreviewVisual _brushPreview;

        public bool IsOpen => _root != null && _root.style.display == DisplayStyle.Flex;

        public void Initialize(
            StrategyCameraController cameraController,
            MapModule mapModule,
            ContentDatabase content,
            SettingsRegistry settings,
            FloatingOrigin floatingOrigin,
            Camera camera,
            IPresentationCommands presentation,
            VisualElement uiRoot,
            VisualElement hudPanel,
            TemporaryChunkDecorations decorations,
            PersonPresenter personPresenter,
            DebugWindowPresenter debugWindow,
            DiscoveriesWindowPresenter discoveriesWindow,
            WorldSelectionPresenter worldSelection,
            Action hideToast)
        {
            _cameraController = cameraController;
            _mapModule = mapModule;
            _content = content;
            _settings = settings;
            _floatingOrigin = floatingOrigin;
            _camera = camera;
            _presentation = presentation;
            _uiRoot = uiRoot;
            _hudPanel = hudPanel;
            _decorations = decorations;
            _personPresenter = personPresenter;
            _debugWindow = debugWindow;
            _discoveriesWindow = discoveriesWindow;
            _worldSelection = worldSelection;
            _hideToast = hideToast;

            _brushPreview = new BrushPreviewVisual(transform);

            BuildPanel();
            uiRoot.Add(_root);
        }

        /// <summary>After a load, the content and map are new instances; a currently-open zoning view is abandoned rather than left showing stale content.</summary>
        public void Rebind(MapModule mapModule, ContentDatabase content, SettingsRegistry settings)
        {
            _mapModule = mapModule;
            _content = content;
            _settings = settings;

            if (IsOpen)
            {
                Close();
            }
        }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            _pausedBeforeOpen = _presentation.IsPaused;
            _presentation.SetPaused(true);

            _hideToast?.Invoke();
            _hudPanel.style.display = DisplayStyle.None;
            _debugWindow?.ForceClose();
            _discoveriesWindow?.ForceCloseForModal();
            _worldSelection?.ForceCloseForModal();

            _decorations?.SetVisible(false);
            _personPresenter?.SetVisible(false);

            _cameraController.EnterZoningView();

            RefreshTypeChoices();
            _nameIsDefault = true;
            _colorIsDefault = true;
            ApplyDefaultsForSelectedType();

            _armed = false;
            _brushPreview.SetActive(false);
            _menuPanel.style.display = DisplayStyle.Flex;
            _root.style.display = DisplayStyle.Flex;
        }

        /// <summary>The actual teardown, once leaving has been allowed (see RequestClose). Also used by Rebind to abandon a stale session outright.</summary>
        private void Close()
        {
            _armed = false;
            _brushPreview.SetActive(false);
            _root.style.display = DisplayStyle.None;

            _cameraController.ExitZoningView();

            _decorations?.SetVisible(true);
            _personPresenter?.SetVisible(true);

            _debugWindow?.ForceReopen();
            _discoveriesWindow?.ForceReopenAfterModal();
            _worldSelection?.ForceReopenAfterModal();
            _hudPanel.style.display = DisplayStyle.Flex;

            _presentation.SetPaused(_pausedBeforeOpen);
        }

        /// <summary>The Close button and Escape (once nothing more local to back out of first) both come here - R18b's hook: if there is unsaved paint, this is where "Discard this zone?" (Yes/No) will gate the close. In R18 there is never paint, so HasUnsavedPaint is always false and this always closes.</summary>
        private void RequestClose()
        {
            if (HasUnsavedPaint())
            {
                // R18b: show "Discard this zone?" (Yes/No) here; only close on Yes.
                return;
            }

            Close();
        }

        /// <summary>R18: always false - there is no paint yet to lose. R18b sets this from the real in-progress paint state.</summary>
        private bool HasUnsavedPaint() => false;

        /// <summary>Escape's entry point - un-arms the brush (back to the menu) if armed, otherwise the same as the Close button.</summary>
        public void HandleEscapePressed()
        {
            if (_armed)
            {
                Unarm();
                return;
            }

            RequestClose();
        }

        /// <summary>The menu panel stays open throughout (Type/Name/Color/brush shape/size can all still be changed, and the ground preview updates at once) - arming only starts showing that ground preview.</summary>
        private void ArmBrush()
        {
            _armed = true;
        }

        private void Unarm()
        {
            _armed = false;
            _brushPreview.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen || !_armed)
            {
                return;
            }

            if (Mouse.current == null || UiInputUtility.IsPointerOverUi(_uiRoot, Mouse.current.position.ReadValue()))
            {
                _brushPreview.SetActive(false);
                return;
            }

            if (!TryGetGroundCell(out CellCoord cell))
            {
                _brushPreview.SetActive(false);
                return;
            }

            double cellSize = _mapModule.CellSizeMeters;
            int sizeCells = _sizeField.value;
            bool round = _shapeField.value == 1;

            // Re-applied every frame (cheap) so changing shape, size or color while armed updates the preview at once.
            _brushPreview.SetShape(round ? BrushShape.Round : BrushShape.Square);
            _brushPreview.SetColor(_colorPicker.Value);

            Vector3 worldCenter;
            float worldSize;
            if (round)
            {
                worldCenter = new Vector3((float)((cell.X + 0.5) * cellSize), 0.03f, (float)((cell.Z + 0.5) * cellSize));
                worldSize = (float)(sizeCells * cellSize);
            }
            else
            {
                double originX = cell.X * cellSize;
                double originZ = cell.Z * cellSize;
                double span = sizeCells * cellSize;
                worldCenter = new Vector3((float)(originX + span * 0.5), 0.03f, (float)(originZ + span * 0.5));
                worldSize = (float)span;
            }

            _brushPreview.SetActive(true);
            _brushPreview.UpdateGeometry(_floatingOrigin.WorldToRender(worldCenter), worldSize);
        }

        private bool TryGetGroundCell(out CellCoord cell)
        {
            cell = default;

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            Ray ray = _camera.ScreenPointToRay(mousePosition);
            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float distance))
            {
                return false;
            }

            Vector3 worldHit = _floatingOrigin.RenderToWorld(ray.GetPoint(distance));
            cell = _mapModule.WorldToCell(worldHit.x, worldHit.z);
            return true;
        }

        #region Menu panel

        private void BuildPanel()
        {
            _root = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    display = DisplayStyle.None
                }
            };

            _menuPanel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    bottom = 0,
                    width = 260,
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 16,
                    paddingRight = 16,
                    paddingTop = 16,
                    paddingBottom = 16,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f)
                }
            };

            _menuPanel.Add(new Label("Zoning") { style = { color = Color.white, fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 12 } });

            _typeField = new DropdownField { label = "Type", style = { marginBottom = 8 } };
            _typeField.RegisterValueChangedCallback(_ => OnTypeChanged());
            _menuPanel.Add(_typeField);

            _nameField = new TextField { label = "Name", style = { marginBottom = 8 } };
            _nameField.RegisterValueChangedCallback(OnNameChanged);
            _menuPanel.Add(_nameField);

            _menuPanel.Add(new Label("Color") { style = { color = Color.white, marginBottom = 4 } });
            _colorPicker = new ColorWheelPicker(OnColorChanged);
            _menuPanel.Add(_colorPicker.Root);

            _menuPanel.Add(new Label("Brush shape") { style = { color = Color.white, marginTop = 8, marginBottom = 4 } });
            _shapeField = new RadioButtonGroup(null, new List<string> { "Square", "Round" }) { value = 0, style = { marginBottom = 8 } };
            _menuPanel.Add(_shapeField);

            int brushMin = (int)_settings.Get(BrushMinSizeCellsSettingId);
            int brushMax = (int)_settings.Get(BrushMaxSizeCellsSettingId);

            var sizeRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 12 } };
            sizeRow.Add(new Label("Brush size") { style = { color = Color.white, marginRight = 8 } });
            _sizeField = new SliderInt(brushMin, brushMax) { value = brushMin, showInputField = true, style = { flexGrow = 1 } };
            sizeRow.Add(_sizeField);
            _menuPanel.Add(sizeRow);

            _menuPanel.Add(new Button(ArmBrush) { text = "Start zoning", style = { height = 32, marginBottom = 6 } });
            _menuPanel.Add(new Button(RequestClose) { text = "Close", style = { height = 32 } });

            _root.Add(_menuPanel);
        }

        private void RefreshTypeChoices()
        {
            _zoneTypes = BuiltInContentTypes.ZoneTypes(_content);
            _typeField.choices = _zoneTypes.Select(t => t.DisplayName).ToList();
            if (_zoneTypes.Count > 0)
            {
                _typeField.index = 0;
            }
        }

        private void OnTypeChanged()
        {
            if (_nameIsDefault)
            {
                ApplyDefaultName();
            }

            if (_colorIsDefault)
            {
                ApplyDefaultColor();
            }
        }

        private void ApplyDefaultsForSelectedType()
        {
            ApplyDefaultName();
            ApplyDefaultColor();
        }

        private void ApplyDefaultName()
        {
            ZoneTypeDefinition selected = SelectedZoneType();
            string baseName = selected != null ? selected.DisplayName + DefaultNameSuffix : "Zone";
            string defaultName = ZoneNaming.NextDefaultName(baseName, GetExistingZoneNames());

            _nameField.SetValueWithoutNotify(defaultName);
            _nameIsDefault = true;
        }

        private void ApplyDefaultColor()
        {
            ZoneTypeDefinition selected = SelectedZoneType();
            Color color = Color.gray;
            if (selected != null && ColorUtility.TryParseHtmlString(selected.ColorHex, out Color parsed))
            {
                color = parsed;
            }

            _colorPicker.SetValue(color);
            _colorIsDefault = true;
        }

        private void OnNameChanged(ChangeEvent<string> evt)
        {
            _nameIsDefault = false;
        }

        private void OnColorChanged(Color color)
        {
            _colorIsDefault = false;
        }

        private ZoneTypeDefinition SelectedZoneType()
        {
            int index = _typeField.index;
            return index >= 0 && index < _zoneTypes.Count ? _zoneTypes[index] : null;
        }

        /// <summary>
        /// R18: there is nowhere yet to look up an existing zone's name -
        /// ZoneState has no Name field until R18b's Create adds one. Until
        /// then this is always empty, so the increment logic is exercised
        /// (and tested) but never actually finds a conflict in play.
        /// </summary>
        private static IEnumerable<string> GetExistingZoneNames() => Array.Empty<string>();

        #endregion
    }
}
