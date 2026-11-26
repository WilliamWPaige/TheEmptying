using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.DevTools;
using TheEmptying.Presentation.Discovery;
using TheEmptying.Presentation.People;
using TheEmptying.Presentation.Selection;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18/R18b: the zoning menu, its top-down view, and painting.
    /// Clicking Zones pauses the game, hides every other window,
    /// switches the camera to the orthographic zoning view, and shows
    /// this menu (Type, Layout [Stockpile only], Name, Color, brush
    /// shape/size). "Start zoning" arms the brush: holding left mouse
    /// paints every cell it touches (right mouse erases cells of the
    /// zone being painted), dragging fast never leaves gaps. Undo steps
    /// back one stroke at a time; Erase all counts as a stroke too.
    /// Create zone (once at least one NxN square fits) confirms, creates
    /// the real zone - laying out its stack spots too, for a stockpile -
    /// posts its event, and returns to the menu ready for the next one.
    /// Leaving with unsaved paint asks to discard it first. R21: the
    /// same menu also opens pre-loaded for an existing zone
    /// (OpenForModify, from its info window's Modify button) - left
    /// paint still adds cells, right erase removes them except any
    /// occupied cell, which refuses with a note instead; the Create
    /// button becomes "Save changes", and a Delete button appears
    /// too. Real box models and loose items all come later.
    /// </summary>
    public sealed class ZoningMenuPresenter : MonoBehaviour
    {
        public const string DefaultNameSuffix = " Zone";
        public const string BrushMinSizeCellsSettingId = "setting.zoning.brush_min_size_cells";
        public const string BrushMaxSizeCellsSettingId = "setting.zoning.brush_max_size_cells";
        public const string CreateMinSquareCellsSettingId = "setting.zoning.create_min_square_cells";
        public const string UndoMaxStepsSettingId = "setting.zoning.undo_max_steps";
        public const string LogPaintingSettingId = "setting.zoning.log_painting";

        /// <summary>R18a: wide enough for every label, field, the 140px-wide color wheel and every button at all three UI size settings, with room to spare - was 260, too narrow (text cut off).</summary>
        private const float MenuPanelWidth = 340f;
        private const float InfoPanelWidth = 220f;

        private StrategyCameraController _cameraController;
        private MapModule _mapModule;
        private ZonesModule _zonesModule;
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
        private Action<string> _showToast;
        private Action _closeSmallPopups;
        private Action _onZoneCreated;
        private Action _onOpenStateChanged;

        private VisualElement _root;
        private VisualElement _menuPanel;
        private VisualElement _infoPanel;
        private Label _infoLabel;
        private Label _createDisabledReasonLabel;
        private Button _createButton;
        private Button _deleteButton;
        private Label _undoBar;
        private VisualElement _createConfirmPanel;
        private Label _createConfirmLabel;
        private VisualElement _discardConfirmPanel;
        private Label _discardConfirmLabel;
        private VisualElement _deleteConfirmPanel;
        private Label _deleteConfirmLabel;

        /// <summary>R21: the zone being modified, if any - null means the ordinary "paint a new zone" flow.</summary>
        private EntityId? _modifyingZoneId;
        private HashSet<CellCoord> _occupiedCellsForModify = new HashSet<CellCoord>();
        private bool _typeChangeLockedBecauseOccupied;
        private HashSet<CellCoord> _modifyOriginalCells = new HashSet<CellCoord>();
        private string _modifyOriginalName;
        private string _modifyOriginalColorHex;
        private string _modifyOriginalTypeId;
        private string _modifyOriginalLayoutId;

        private DropdownField _typeField;
        private Label _layoutLabel;
        private DropdownField _layoutField;
        private TextField _nameField;
        private ColorWheelPicker _colorPicker;
        private RadioButtonGroup _shapeField;
        private SliderInt _sizeField;

        private List<ZoneTypeDefinition> _zoneTypes = new List<ZoneTypeDefinition>();
        private readonly List<string> _layoutIds = new List<string> { ZonesModule.LayoutNaturalId, ZonesModule.LayoutUniformId };
        private bool _nameIsDefault = true;
        private bool _colorIsDefault = true;
        private bool _pausedBeforeOpen;
        private bool _armed;

        private BrushPreviewVisual _brushPreview;
        private ZoneVisual _paintVisual;
        private readonly HashSet<CellCoord> _paintedCells = new HashSet<CellCoord>();
        private ZonePaintUndoStack _undoStack;

        private bool _isPainting;
        private bool _isErasing;
        private CellCoord? _lastDragCell;

        /// <summary>R18c: debug-log bookkeeping for one continuous mouse hold (see LogPaintingSettingId) - reset when the button goes down, reported once when it comes back up.</summary>
        private bool _paintLogStrokeActive;
        private bool _paintLogStrokeOverUi;
        private string _paintLogStrokeUiElement;
        private bool _paintLogStrokeFoundGround;
        private int _paintLogStrokeCellsAtStart;

        /// <summary>
        /// R18e: whether the mouse button currently held was pressed while the pointer was over
        /// the ground, not any UI element or popup - set once, the frame the hold began, and left
        /// alone for the rest of that hold even if the pointer (or the UI under it) later changes.
        /// A press that began on UI never paints, even once it reaches the ground or the UI it
        /// started on closes mid-hold (the Create confirm's "Yes" button, most notably). Tracked
        /// every frame _armed is true - including while a confirm popup blocks everything else -
        /// so this can't go stale across the one frame a popup disappears while still held.
        /// </summary>
        private bool _wasMouseHeld;
        private bool _strokeStartedOnGround;

        public bool IsOpen => _root != null && _root.style.display == DisplayStyle.Flex;

        public void Initialize(
            StrategyCameraController cameraController,
            MapModule mapModule,
            ZonesModule zonesModule,
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
            Action hideToast,
            Action<string> showToast,
            Action closeSmallPopups,
            Action onZoneCreated,
            Action onOpenStateChanged)
        {
            _cameraController = cameraController;
            _mapModule = mapModule;
            _zonesModule = zonesModule;
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
            _showToast = showToast;
            _closeSmallPopups = closeSmallPopups;
            _onZoneCreated = onZoneCreated;
            _onOpenStateChanged = onOpenStateChanged;

            _brushPreview = new BrushPreviewVisual(transform);
            _paintVisual = new ZoneVisual(transform, Color.white, collidable: false);
            _floatingOrigin.Shifted += delta => transform.position -= delta;

            BuildPanel();
            uiRoot.Add(_root);
        }

        /// <summary>After a load, the content, map and zones are new instances; a currently-open zoning view is abandoned rather than left showing stale content.</summary>
        public void Rebind(MapModule mapModule, ZonesModule zonesModule, ContentDatabase content, SettingsRegistry settings)
        {
            _mapModule = mapModule;
            _zonesModule = zonesModule;
            _content = content;
            _settings = settings;

            if (IsOpen)
            {
                AbandonPaint();
                Close();
            }
        }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            EnterZoningViewCommon();

            _modifyingZoneId = null;
            _occupiedCellsForModify.Clear();
            _typeChangeLockedBecauseOccupied = false;
            _createButton.text = "Create zone";
            _deleteButton.style.display = DisplayStyle.None;

            _undoStack = new ZonePaintUndoStack((int)_settings.Get(UndoMaxStepsSettingId));
            _paintedCells.Clear();

            RefreshTypeChoices();
            _typeField.SetEnabled(true);
            _layoutField.index = 0;
            RefreshLayoutVisibility();
            _nameIsDefault = true;
            _colorIsDefault = true;
            ApplyDefaultsForSelectedType();

            FinishOpening();
        }

        /// <summary>
        /// R21: the selected zone's info window's Modify button - opens
        /// the same menu with this zone's type, name, color, layout and
        /// cells already loaded, its tint shown as the live paint.
        /// </summary>
        public void OpenForModify(EntityId zoneId)
        {
            if (IsOpen || !_zonesModule.TryGetZone(zoneId, out ZoneState zone))
            {
                return;
            }

            EnterZoningViewCommon();

            _modifyingZoneId = zoneId;
            _occupiedCellsForModify = _zonesModule.GetOccupiedCells(zoneId);
            _typeChangeLockedBecauseOccupied = _zonesModule.IsZoneOccupied(zoneId);
            _createButton.text = "Save changes";
            _deleteButton.style.display = DisplayStyle.Flex;

            _undoStack = new ZonePaintUndoStack((int)_settings.Get(UndoMaxStepsSettingId));
            _paintedCells.Clear();
            foreach (CellCoord cell in zone.Cells)
            {
                _paintedCells.Add(cell);
            }

            RefreshTypeChoices();
            SelectTypeById(zone.ZoneTypeId);
            _typeField.SetEnabled(true);
            _layoutField.index = Math.Max(0, _layoutIds.IndexOf(zone.LayoutId));
            RefreshLayoutVisibility();

            _nameField.SetValueWithoutNotify(zone.Name ?? string.Empty);
            _nameIsDefault = false;

            Color color = Color.gray;
            if (!string.IsNullOrEmpty(zone.ColorHex) && ColorUtility.TryParseHtmlString(zone.ColorHex, out Color parsed))
            {
                color = parsed;
            }

            _colorPicker.SetValue(color);
            _colorIsDefault = false;

            _modifyOriginalCells = new HashSet<CellCoord>(zone.Cells);
            _modifyOriginalName = zone.Name;
            _modifyOriginalColorHex = zone.ColorHex;
            _modifyOriginalTypeId = zone.ZoneTypeId;
            _modifyOriginalLayoutId = zone.LayoutId;

            RefreshPaintVisual();
            FinishOpening();
        }

        /// <summary>The side effects shared by Open and OpenForModify - pausing, hiding every other window, switching to the zoning camera.</summary>
        private void EnterZoningViewCommon()
        {
            _pausedBeforeOpen = _presentation.IsPaused;
            _presentation.SetPaused(true);

            _hideToast?.Invoke();
            _closeSmallPopups?.Invoke();
            _hudPanel.style.display = DisplayStyle.None;
            _debugWindow?.ForceClose();
            _discoveriesWindow?.ForceCloseForModal();
            _worldSelection?.ForceCloseForModal();

            _decorations?.SetVisible(false);
            _personPresenter?.SetVisible(false);

            _cameraController.EnterZoningView();
        }

        /// <summary>The side effects shared by Open and OpenForModify, once each has set up its own starting state.</summary>
        private void FinishOpening()
        {
            _armed = false;
            _wasMouseHeld = false;
            _strokeStartedOnGround = false;
            _brushPreview.SetActive(false);
            CloseCreateConfirm();
            CloseDiscardConfirm();
            CloseDeleteConfirm();
            _menuPanel.style.display = DisplayStyle.Flex;
            _infoPanel.style.display = DisplayStyle.Flex;
            _root.style.display = DisplayStyle.Flex;

            _onOpenStateChanged?.Invoke();
        }

        private void SelectTypeById(string typeId)
        {
            int index = _zoneTypes.FindIndex(t => t.Id == typeId);
            _typeField.index = index >= 0 ? index : 0;
        }

        /// <summary>The actual teardown, once leaving has been allowed (see RequestClose). Also used by Rebind to abandon a stale session outright.</summary>
        private void Close()
        {
            _armed = false;
            _isPainting = false;
            _isErasing = false;
            _lastDragCell = null;
            _wasMouseHeld = false;
            _strokeStartedOnGround = false;
            _brushPreview.SetActive(false);
            _paintVisual.SetActive(false);
            _root.style.display = DisplayStyle.None;

            _cameraController.ExitZoningView();

            _decorations?.SetVisible(true);
            _personPresenter?.SetVisible(true);

            _debugWindow?.ForceReopen();
            _discoveriesWindow?.ForceReopenAfterModal();
            _worldSelection?.ForceReopenAfterModal();
            _hudPanel.style.display = DisplayStyle.Flex;

            _presentation.SetPaused(_pausedBeforeOpen);

            _onOpenStateChanged?.Invoke();
        }

        /// <summary>The Close button and Escape (once nothing more local to back out of first) both come here - leaving with unsaved paint (or, modifying, any actual unsaved change) asks to discard first.</summary>
        private void RequestClose()
        {
            if (HasUnsavedChanges())
            {
                ShowDiscardConfirm();
                return;
            }

            Close();
        }

        /// <summary>R21: for a new zone, any paint at all is unsaved; modifying an existing one, only an actual change against what was loaded counts - the zone itself always starts "painted" with its own cells.</summary>
        private bool HasUnsavedChanges()
        {
            if (!_modifyingZoneId.HasValue)
            {
                return _paintedCells.Count > 0;
            }

            ZoneTypeDefinition selected = SelectedZoneType();
            string selectedTypeId = selected != null ? selected.Id : _modifyOriginalTypeId;
            bool layoutChanged = selectedTypeId == ZonesModule.StockpileTypeId
                && SelectedLayoutId() != (_modifyOriginalLayoutId ?? ZonesModule.LayoutNaturalId);

            return !_paintedCells.SetEquals(_modifyOriginalCells)
                || _nameField.value != _modifyOriginalName
                || "#" + ColorUtility.ToHtmlStringRGB(_colorPicker.Value) != _modifyOriginalColorHex
                || selectedTypeId != _modifyOriginalTypeId
                || layoutChanged;
        }

        /// <summary>Escape's entry point - closes an open confirm popup, un-arms the brush (back to the menu) if armed, otherwise the same as the Close button.</summary>
        public void HandleEscapePressed()
        {
            if (_createConfirmPanel.style.display == DisplayStyle.Flex)
            {
                CloseCreateConfirm();
                return;
            }

            if (_discardConfirmPanel.style.display == DisplayStyle.Flex)
            {
                CloseDiscardConfirm();
                return;
            }

            if (_deleteConfirmPanel.style.display == DisplayStyle.Flex)
            {
                CloseDeleteConfirm();
                return;
            }

            if (_armed)
            {
                Unarm();
                return;
            }

            RequestClose();
        }

        private void ArmBrush()
        {
            _armed = true;
            _wasMouseHeld = false;
            _strokeStartedOnGround = false;
        }

        private void Unarm()
        {
            _armed = false;
            _isPainting = false;
            _isErasing = false;
            _lastDragCell = null;
            _wasMouseHeld = false;
            _strokeStartedOnGround = false;
            _brushPreview.SetActive(false);
        }

        private void AbandonPaint()
        {
            _paintedCells.Clear();
            _undoStack.Clear();
            _paintVisual.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            RefreshInfoAndCreateButton();
            RefreshUndoBar();

            // Every frame while open (not just while armed), so changing color in the menu recolors the paint at once.
            Color pickerColor = _colorPicker.Value;
            _paintVisual.SetColor(new Color(pickerColor.r, pickerColor.g, pickerColor.b, ZonePresenter.TintAlpha));

            if (!_armed)
            {
                return;
            }

            bool mouseHeld = Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed);
            VisualElement uiHit = Mouse.current != null ? UiInputUtility.PickOverUi(_uiRoot, Mouse.current.position.ReadValue()) : null;
            bool popupOpen = _createConfirmPanel.style.display == DisplayStyle.Flex || _discardConfirmPanel.style.display == DisplayStyle.Flex || _deleteConfirmPanel.style.display == DisplayStyle.Flex;
            bool overUi = popupOpen || uiHit != null;

            bool freshPress = mouseHeld && !_wasMouseHeld;
            if (freshPress)
            {
                LogPaintPressDiagnostics(uiHit);
            }

            // Tracked every frame while armed - including while a popup blocks everything else
            // below - so a press that started on the popup is never mistaken for a fresh one
            // that started on the ground the moment the popup closes while still held.
            TrackPaintLogStroke(mouseHeld);
            TrackPaintStrokeOrigin(mouseHeld, overUi);

            if (popupOpen)
            {
                _brushPreview.SetActive(false);
                EndDragIfAny();
                return;
            }

            if (Mouse.current == null)
            {
                _brushPreview.SetActive(false);
                EndDragIfAny();
                return;
            }

            if (uiHit != null)
            {
                RecordPaintLogOverUi(uiHit);
                _brushPreview.SetActive(false);
                EndDragIfAny();
                return;
            }

            if (!TryGetGroundCell(out CellCoord cell))
            {
                _brushPreview.SetActive(false);
                EndDragIfAny();
                return;
            }

            RecordPaintLogGroundFound();

            int sizeCells = _sizeField.value;
            BrushShape shape = _shapeField.value == 1 ? BrushShape.Round : BrushShape.Square;

            UpdateBrushPreview(cell, shape, sizeCells);

            if (_strokeStartedOnGround)
            {
                UpdatePaintDrag(cell, shape, sizeCells);
            }
            else
            {
                EndDragIfAny();
            }
        }

        /// <summary>R18e: records, the frame a mouse hold begins, whether the pointer was over the ground or some UI element/popup - see _strokeStartedOnGround.</summary>
        private void TrackPaintStrokeOrigin(bool mouseHeld, bool overUi)
        {
            if (mouseHeld && !_wasMouseHeld)
            {
                _strokeStartedOnGround = !overUi;
            }

            _wasMouseHeld = mouseHeld;
        }

        #region Paint debug log

        /// <summary>R18c: starts a new logged stroke the frame a mouse button goes down, and reports it the frame it comes back up - behind LogPaintingSettingId (on by default in the Editor), so the user can confirm by hand whether a stroke was blocked by the UI check or the ground raycast, and how many cells it actually changed.</summary>
        private void TrackPaintLogStroke(bool mouseHeld)
        {
            if (mouseHeld && !_paintLogStrokeActive)
            {
                _paintLogStrokeActive = true;
                _paintLogStrokeOverUi = false;
                _paintLogStrokeUiElement = null;
                _paintLogStrokeFoundGround = false;
                _paintLogStrokeCellsAtStart = _paintedCells.Count;
            }
            else if (!mouseHeld && _paintLogStrokeActive)
            {
                _paintLogStrokeActive = false;
                ReportPaintLogStroke();
            }
        }

        /// <summary>
        /// R18g: on every fresh press (behind LogPaintingSettingId), logs the raw mouse screen
        /// position, the converted panel position and what it picked, the info box's and menu's
        /// own on-screen rectangles, and the ground cell under the cursor - so the converted
        /// position landing inside whichever rectangle is actually under the cursor (and not its
        /// mirror image) can be confirmed directly, at any UI size or Game view size.
        /// </summary>
        private void LogPaintPressDiagnostics(VisualElement uiHit)
        {
            if (_settings.Get(LogPaintingSettingId) == 0 || Mouse.current == null)
            {
                return;
            }

            Vector2 screenPosition = Mouse.current.position.ReadValue();
            Vector2 panelPosition = _uiRoot.panel != null
                ? RuntimePanelUtils.ScreenToPanel(_uiRoot.panel, UiInputUtility.FlipScreenY(screenPosition, Screen.height))
                : Vector2.zero;
            bool groundFound = TryGetGroundCell(out CellCoord cell);
            string groundDescription = groundFound ? $"({cell.X}, {cell.Z})" : "none";

            Debug.Log(
                $"[Zoning] Paint press: screen = {screenPosition}, converted panel position = {panelPosition}, " +
                $"pick = {DescribeElement(uiHit)}, info box rect = {_infoPanel.worldBound}, " +
                $"menu rect = {_menuPanel.worldBound}, ground cell = {groundDescription}");
        }

        private static string DescribeElement(VisualElement element)
        {
            if (element == null)
            {
                return "none";
            }

            return string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name;
        }

        private void RecordPaintLogOverUi(VisualElement uiHit)
        {
            if (!_paintLogStrokeActive)
            {
                return;
            }

            _paintLogStrokeOverUi = true;
            _paintLogStrokeUiElement ??= string.IsNullOrEmpty(uiHit.name) ? uiHit.GetType().Name : uiHit.name;
        }

        private void RecordPaintLogGroundFound()
        {
            if (_paintLogStrokeActive)
            {
                _paintLogStrokeFoundGround = true;
            }
        }

        private void ReportPaintLogStroke()
        {
            if (_settings.Get(LogPaintingSettingId) == 0)
            {
                return;
            }

            int cellsChanged = _paintedCells.Count - _paintLogStrokeCellsAtStart;
            string uiDescription = _paintLogStrokeOverUi ? $"yes ({_paintLogStrokeUiElement ?? "unnamed element"})" : "no";
            Debug.Log($"[Zoning] Paint stroke: over UI = {uiDescription}, ground cell found = {_paintLogStrokeFoundGround}, cells changed = {cellsChanged}");

            // R18d: temporary - lets the user confirm by hand that the painted mesh is actually
            // drawing where the brush is, rather than off-screen under a floating-origin offset.
            if (_paintedCells.Count > 0)
            {
                var paintRenderer = _paintVisual.GameObject.GetComponent<MeshRenderer>();
                Vector3 meshBoundsCenter = paintRenderer.bounds.center;
                Vector3 brushPosition = _brushPreview.GameObject.transform.position;
                Debug.Log($"[Zoning] Paint mesh world bounds center = {meshBoundsCenter}, brush preview world position = {brushPosition}");
            }
        }

        #endregion

        private void UpdateBrushPreview(CellCoord cell, BrushShape shape, int sizeCells)
        {
            double cellSize = _mapModule.CellSizeMeters;
            _brushPreview.SetShape(shape);
            _brushPreview.SetColor(_colorPicker.Value);

            Vector3 worldCenter;
            float worldSize;
            if (shape == BrushShape.Round)
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

        private void UpdatePaintDrag(CellCoord cell, BrushShape shape, int sizeCells)
        {
            bool leftHeld = Mouse.current.leftButton.isPressed;
            bool rightHeld = Mouse.current.rightButton.isPressed;

            if (!leftHeld && !rightHeld)
            {
                EndDragIfAny();
                return;
            }

            bool erasing = rightHeld && !leftHeld;
            bool startingNewStroke = (!_isPainting && !_isErasing) || (erasing != _isErasing);

            if (startingNewStroke)
            {
                PushUndoSnapshot();
                _isPainting = !erasing;
                _isErasing = erasing;
                _lastDragCell = null;
            }

            IEnumerable<CellCoord> anchors = _lastDragCell.HasValue ? ZonePaintGeometry.LineCells(_lastDragCell.Value, cell) : new[] { cell };
            bool changed = false;

            bool triedToEraseOccupied = false;

            foreach (CellCoord anchor in anchors)
            {
                foreach (CellCoord target in ZonePaintGeometry.BrushFootprint(anchor, shape, sizeCells))
                {
                    if (erasing)
                    {
                        if (_occupiedCellsForModify.Contains(target))
                        {
                            triedToEraseOccupied = true;
                        }
                        else
                        {
                            changed |= _paintedCells.Remove(target);
                        }
                    }
                    else if (!_paintedCells.Contains(target) && _zonesModule.CanPaintCell(target, _modifyingZoneId))
                    {
                        _paintedCells.Add(target);
                        changed = true;
                    }
                }
            }

            if (triedToEraseOccupied)
            {
                _showToast?.Invoke("Occupied: move the items out first.");
            }

            _lastDragCell = cell;

            if (changed)
            {
                RefreshPaintVisual();
            }
        }

        private void EndDragIfAny()
        {
            _isPainting = false;
            _isErasing = false;
            _lastDragCell = null;
        }

        private void PushUndoSnapshot()
        {
            _undoStack.Push(_paintedCells);
        }

        private void Undo()
        {
            if (!_undoStack.TryUndo(out HashSet<CellCoord> previous))
            {
                return;
            }

            _paintedCells.Clear();
            foreach (CellCoord cell in previous)
            {
                _paintedCells.Add(cell);
            }

            RefreshPaintVisual();
        }

        /// <summary>Clears every unoccupied painted cell; an occupied one (modifying a stockpile with something in it) is never touched.</summary>
        private void EraseAll()
        {
            List<CellCoord> toRemove = _paintedCells.Where(c => !_occupiedCellsForModify.Contains(c)).ToList();
            if (toRemove.Count == 0)
            {
                return;
            }

            PushUndoSnapshot();
            foreach (CellCoord cell in toRemove)
            {
                _paintedCells.Remove(cell);
            }

            RefreshPaintVisual();
        }

        private void RefreshPaintVisual()
        {
            if (_paintedCells.Count == 0)
            {
                _paintVisual.SetActive(false);
                return;
            }

            _paintVisual.SetActive(true);
            _paintVisual.UpdateGeometry(_paintedCells, _mapModule.CellSizeMeters);
        }

        private void RefreshUndoBar()
        {
            _undoBar.text = $"Undo: {_undoStack.Count} / {_undoStack.MaxSteps}";
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

        #region Info box and Create

        private void RefreshInfoAndCreateButton()
        {
            ZoneTypeDefinition selected = SelectedZoneType();
            double cellSize = _mapModule.CellSizeMeters;
            double areaSquareMeters = _paintedCells.Count * cellSize * cellSize;

            _infoLabel.text = $"{(selected != null ? selected.DisplayName : "-")}\n{_nameField.value}\n{areaSquareMeters:0.#} m²";

            int minSquare = Math.Max(1, (int)_settings.Get(CreateMinSquareCellsSettingId));
            bool canCreate = ZonePaintGeometry.ContainsSquare(_paintedCells, minSquare);

            _createButton.SetEnabled(canCreate);
            _createDisabledReasonLabel.text = _modifyingZoneId.HasValue ? "Too small; use Delete instead" : "Too small";

            // R18f: toggles visibility, not display - "Too small" reserves its line's height
            // whether it's shown or not, so the info box's height (and the position of
            // everything in it, including the Create button) never shifts under the cursor
            // as painting crosses the minimum-square threshold back and forth.
            _createDisabledReasonLabel.style.visibility = canCreate ? Visibility.Hidden : Visibility.Visible;
        }

        private void OpenCreateConfirm()
        {
            ZoneTypeDefinition selected = SelectedZoneType();
            double cellSize = _mapModule.CellSizeMeters;
            double areaSquareMeters = _paintedCells.Count * cellSize * cellSize;

            _createConfirmLabel.text = _modifyingZoneId.HasValue
                ? $"Save changes to \"{_nameField.value}\"?"
                : $"Create {(selected != null ? selected.DisplayName : "zone")} \"{_nameField.value}\", {areaSquareMeters:0.#} m²?";
            _createConfirmPanel.style.display = DisplayStyle.Flex;
        }

        private void CloseCreateConfirm()
        {
            _createConfirmPanel.style.display = DisplayStyle.None;
        }

        private void ConfirmCreateZone()
        {
            CloseCreateConfirm();

            ZoneTypeDefinition selected = SelectedZoneType();
            string zoneTypeId = selected != null ? selected.Id : ZonesModule.StockpileTypeId;
            string colorHex = "#" + ColorUtility.ToHtmlStringRGB(_colorPicker.Value);

            if (_modifyingZoneId.HasValue)
            {
                _zonesModule.ApplyZoneEdit(_modifyingZoneId.Value, _nameField.value, colorHex, zoneTypeId, _paintedCells, SelectedLayoutId());
            }
            else
            {
                _zonesModule.CreateZone(zoneTypeId, _nameField.value, colorHex, _paintedCells, SelectedLayoutId());
            }

            _onZoneCreated?.Invoke();
            ReturnToBlankCreateState();
        }

        private void ShowDiscardConfirm()
        {
            _discardConfirmLabel.text = _modifyingZoneId.HasValue ? "Discard changes?" : "Discard this zone?";
            _discardConfirmPanel.style.display = DisplayStyle.Flex;
        }

        private void CloseDiscardConfirm()
        {
            _discardConfirmPanel.style.display = DisplayStyle.None;
        }

        /// <summary>Yes on "Discard changes?"/"Discard this zone?" - nothing was ever saved, so simply abandoning the local paint and closing already leaves the real zone exactly as it was.</summary>
        private void ConfirmDiscardAndClose()
        {
            CloseDiscardConfirm();
            AbandonPaint();
            Close();
        }

        /// <summary>R21: the menu's own Delete button, shown only while modifying - "Delete <name>? This can't be undone."</summary>
        private void OpenDeleteConfirm()
        {
            if (!_modifyingZoneId.HasValue)
            {
                return;
            }

            _deleteConfirmLabel.text = $"Delete \"{_nameField.value}\"? This can't be undone.";
            _deleteConfirmPanel.style.display = DisplayStyle.Flex;
        }

        private void CloseDeleteConfirm()
        {
            _deleteConfirmPanel.style.display = DisplayStyle.None;
        }

        private void ConfirmDeleteZone()
        {
            CloseDeleteConfirm();

            if (!_modifyingZoneId.HasValue)
            {
                return;
            }

            ZoneDeleteResult result = _zonesModule.DeleteZone(_modifyingZoneId.Value);
            _onZoneCreated?.Invoke();

            if (!result.FullyDeleted)
            {
                _showToast?.Invoke($"Could not delete {result.RemainingOccupiedSpots} spot(s) - occupied. Empty them, then delete again.");
            }

            ReturnToBlankCreateState();
        }

        /// <summary>After saving, or deleting, from a Modify session - back to the plain "paint a new zone" state, ready for the next one, same as the ordinary create flow leaves it.</summary>
        private void ReturnToBlankCreateState()
        {
            AbandonPaint();
            Unarm();
            _modifyingZoneId = null;
            _occupiedCellsForModify.Clear();
            _typeChangeLockedBecauseOccupied = false;
            _createButton.text = "Create zone";
            _deleteButton.style.display = DisplayStyle.None;
            _typeField.SetEnabled(true);

            RefreshTypeChoices();
            _layoutField.index = 0;
            RefreshLayoutVisibility();
            ApplyDefaultsForSelectedType();
        }

        #endregion

        #region Menu panel

        private void BuildPanel()
        {
            _root = new VisualElement
            {
                // R18c: this covers the whole screen so the menu/info panels inside it can be
                // positioned, but it must never be a pick target itself - a full-screen element
                // with the default pickingMode (Position) is hit by every screen point that
                // isn't over one of its real children, so every click (including paint strokes
                // over the open ground) was being swallowed as "over UI" before it ever reached
                // the world. The menu/info panels and their buttons keep the default picking.
                pickingMode = PickingMode.Ignore,
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

            BuildMenuPanel();
            BuildInfoPanel();
            BuildCreateConfirmPanel();
            BuildDiscardConfirmPanel();
            BuildDeleteConfirmPanel();
        }

        private void BuildMenuPanel()
        {
            _menuPanel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    bottom = 0,
                    width = MenuPanelWidth,
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 16,
                    paddingRight = 16,
                    paddingTop = 16,
                    paddingBottom = 16,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f)
                }
            };

            _menuPanel.Add(new Label("Zoning") { style = { color = Color.white, fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 12 } });

            // No .label set on any field below - the default runtime theme reserves a fixed-width slot
            // (meant for editor-style inspectors) for a field's own built-in label, which left almost no
            // room for the value in a narrow panel. A plain Label placed above instead costs nothing.
            _menuPanel.Add(new Label("Type") { style = { color = Color.white, marginBottom = 4 } });
            _typeField = new DropdownField { style = { marginBottom = 8 } };
            _typeField.RegisterValueChangedCallback(_ => OnTypeChanged());
            _menuPanel.Add(_typeField);

            // R20: shown only for Stockpile (see RefreshLayoutVisibility) - which layout its stack spots are laid out with.
            _layoutLabel = new Label("Layout") { style = { color = Color.white, marginBottom = 4 } };
            _menuPanel.Add(_layoutLabel);
            _layoutField = new DropdownField(new List<string> { "Natural", "Uniform" }, 0) { style = { marginBottom = 8 } };
            _menuPanel.Add(_layoutField);

            _menuPanel.Add(new Label("Name") { style = { color = Color.white, marginBottom = 4 } });
            _nameField = new TextField { style = { marginBottom = 8 } };
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

            _menuPanel.Add(new Label("Brush size") { style = { color = Color.white, marginBottom = 4 } });
            _sizeField = new SliderInt(brushMin, brushMax) { value = brushMin, showInputField = true, style = { marginBottom = 12 } };
            _menuPanel.Add(_sizeField);

            _menuPanel.Add(new Button(ArmBrush) { text = "Start zoning", style = { height = 32, marginBottom = 6 } });

            var undoRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            undoRow.Add(new Button(Undo) { text = "Undo", style = { height = 32, flexGrow = 1, marginRight = 6 } });
            undoRow.Add(new Button(EraseAll) { text = "Erase all", style = { height = 32, flexGrow = 1 } });
            _menuPanel.Add(undoRow);

            _undoBar = new Label { style = { color = new Color(0.8f, 0.8f, 0.8f), marginBottom = 12 } };
            _menuPanel.Add(_undoBar);

            // R21: only shown while modifying an existing zone - a new, unsaved zone has nothing to delete yet.
            _deleteButton = new Button(OpenDeleteConfirm) { text = "Delete", style = { height = 32, marginBottom = 6, display = DisplayStyle.None } };
            _menuPanel.Add(_deleteButton);

            _menuPanel.Add(new Button(RequestClose) { text = "Close", style = { height = 32 } });

            _root.Add(_menuPanel);
        }

        private void BuildInfoPanel()
        {
            _infoPanel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = MenuPanelWidth + 12,
                    top = 0,
                    width = InfoPanelWidth,
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 16,
                    paddingRight = 16,
                    paddingTop = 16,
                    paddingBottom = 16,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f)
                }
            };

            _infoLabel = new Label { style = { color = Color.white, whiteSpace = WhiteSpace.Normal, marginBottom = 8 } };
            _infoPanel.Add(_infoLabel);

            _createButton = new Button(OpenCreateConfirm) { text = "Create zone", style = { height = 32, marginBottom = 4 } };
            _infoPanel.Add(_createButton);

            _createDisabledReasonLabel = new Label("Too small") { style = { color = new Color(0.9f, 0.4f, 0.4f), visibility = Visibility.Hidden } };
            _infoPanel.Add(_createDisabledReasonLabel);

            _root.Add(_infoPanel);
        }

        private void BuildCreateConfirmPanel()
        {
            _createConfirmPanel = new VisualElement
            {
                // R18c: same reason as _root - this is just a full-screen centering container,
                // not a real panel; the actual dialog box (with its background and buttons)
                // is the child added below, which keeps the default picking.
                pickingMode = PickingMode.Ignore,
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
                    backgroundColor = new Color(0f, 0f, 0f, 0.75f)
                }
            };

            _createConfirmLabel = new Label { style = { color = Color.white, marginBottom = 12, whiteSpace = WhiteSpace.Normal, maxWidth = 320 } };
            panel.Add(_createConfirmLabel);

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttonRow.Add(new Button(ConfirmCreateZone) { text = "Yes", style = { width = 100, height = 32, marginRight = 8 } });
            buttonRow.Add(new Button(CloseCreateConfirm) { text = "No", style = { width = 100, height = 32 } });
            panel.Add(buttonRow);

            _createConfirmPanel.Add(panel);
            _root.Add(_createConfirmPanel);
        }

        private void BuildDiscardConfirmPanel()
        {
            _discardConfirmPanel = new VisualElement
            {
                // R18c: same reason as _root - this is just a full-screen centering container.
                pickingMode = PickingMode.Ignore,
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
                    backgroundColor = new Color(0f, 0f, 0f, 0.75f)
                }
            };

            _discardConfirmLabel = new Label { style = { color = Color.white, marginBottom = 12 } };
            panel.Add(_discardConfirmLabel);

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttonRow.Add(new Button(ConfirmDiscardAndClose) { text = "Yes", style = { width = 100, height = 32, marginRight = 8 } });
            buttonRow.Add(new Button(CloseDiscardConfirm) { text = "No", style = { width = 100, height = 32 } });
            panel.Add(buttonRow);

            _discardConfirmPanel.Add(panel);
            _root.Add(_discardConfirmPanel);
        }

        private void BuildDeleteConfirmPanel()
        {
            _deleteConfirmPanel = new VisualElement
            {
                // R18c: same reason as _root - this is just a full-screen centering container.
                pickingMode = PickingMode.Ignore,
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
                    backgroundColor = new Color(0f, 0f, 0f, 0.75f)
                }
            };

            _deleteConfirmLabel = new Label { style = { color = Color.white, marginBottom = 12, whiteSpace = WhiteSpace.Normal, maxWidth = 320 } };
            panel.Add(_deleteConfirmLabel);

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttonRow.Add(new Button(ConfirmDeleteZone) { text = "Yes", style = { width = 100, height = 32, marginRight = 8 } });
            buttonRow.Add(new Button(CloseDeleteConfirm) { text = "No", style = { width = 100, height = 32 } });
            panel.Add(buttonRow);

            _deleteConfirmPanel.Add(panel);
            _root.Add(_deleteConfirmPanel);
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
            if (_typeChangeLockedBecauseOccupied)
            {
                ZoneTypeDefinition selected = SelectedZoneType();
                if (selected == null || selected.Id != _modifyOriginalTypeId)
                {
                    // R21: "a stockpile holding any items can't change type" - snap back and say why.
                    SelectTypeById(_modifyOriginalTypeId);
                    _showToast?.Invoke("Empty this stockpile first.");
                    return;
                }
            }

            if (_nameIsDefault)
            {
                ApplyDefaultName();
            }

            if (_colorIsDefault)
            {
                ApplyDefaultColor();
            }

            RefreshLayoutVisibility();
        }

        /// <summary>R20: the Layout dropdown only means anything for a Stockpile - hidden for every other zone type.</summary>
        private void RefreshLayoutVisibility()
        {
            ZoneTypeDefinition selected = SelectedZoneType();
            DisplayStyle display = selected != null && selected.Id == ZonesModule.StockpileTypeId ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutLabel.style.display = display;
            _layoutField.style.display = display;
        }

        private string SelectedLayoutId()
        {
            int index = _layoutField.index;
            return index >= 0 && index < _layoutIds.Count ? _layoutIds[index] : ZonesModule.LayoutNaturalId;
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
            string defaultName = ZoneNaming.NextDefaultName(baseName, _zonesModule.ZoneNames);

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

        #endregion
    }
}
