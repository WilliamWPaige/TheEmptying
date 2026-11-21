using System;
using System.Collections.Generic;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Land
{
    /// <summary>
    /// Gray-box rendering for land: a zone tool (buttons, drag-to-paint,
    /// right-click to cancel), a toggleable zone color overlay, plot
    /// outlines with owner names, and the crown grounds outlined. Reads the
    /// module, never writes to it except through RequestSetZone/RemoveZone.
    /// </summary>
    public sealed class LandPresenter : MonoBehaviour
    {
        private const float OverlayHeight = 0.05f;
        private const float PlotOutlineHeight = 0.08f;

        private static readonly Dictionary<string, Color> ZoneColors = new Dictionary<string, Color>
        {
            { "zone.residential_low", new Color(0.3f, 0.7f, 0.3f, 0.45f) },
            { "zone.residential_middle", new Color(0.3f, 0.55f, 0.75f, 0.45f) },
            { "zone.residential_high", new Color(0.65f, 0.4f, 0.75f, 0.45f) },
            { "zone.commercial", new Color(0.85f, 0.75f, 0.2f, 0.45f) },
            { "zone.industrial", new Color(0.75f, 0.45f, 0.2f, 0.45f) },
            { "zone.farm", new Color(0.55f, 0.75f, 0.25f, 0.45f) }
        };

        private LandModule _land;
        private PeopleModule _peopleModule;
        private Camera _camera;
        private InputAction _selectAction;
        private Action<EntityId> _onPlotSelected;
        private VisualElement _uiRoot;

        private VisualElement _toolPanel;
        private bool _overlayVisible;
        private string _selectedZoneId = "zone.residential_low";
        private bool _toolActive;
        private bool _dragging;
        private GridPoint _dragStart;

        private GameObject _overlayPlane;
        private Texture2D _overlayTexture;
        private bool _overlayDirty = true;

        private GameObject _crownGroundsOutline;
        private readonly Dictionary<EntityId, GameObject> _plotOutlines = new Dictionary<EntityId, GameObject>();

        public void Initialize(LandModule land, MapGrid grid, Camera camera, InputActionMap gameplayMap, VisualElement uiRoot, Action<EntityId> onPlotSelected)
        {
            _land = land;
            _cachedGrid = grid;
            _camera = camera;
            _selectAction = gameplayMap.FindAction("Select");
            _onPlotSelected = onPlotSelected;
            _uiRoot = uiRoot;

            BuildToolPanel(uiRoot);
            BuildOverlayPlane();
            BuildCrownGroundsOutline();
        }

        /// <summary>Call again after a load swaps in a new Simulation (and a new module instance).</summary>
        public void Rebind(LandModule land, MapGrid grid)
        {
            _land = land;
            _cachedGrid = grid;
            _overlayDirty = true;

            foreach (GameObject marker in _plotOutlines.Values)
            {
                Destroy(marker);
            }

            _plotOutlines.Clear();
        }

        /// <summary>Optional: plot labels show the owner or leaseholder's name instead of a bare entity id.</summary>
        public void SetPeopleModule(PeopleModule peopleModule) => _peopleModule = peopleModule;

        private void Update()
        {
            if (_overlayDirty)
            {
                RebuildOverlayTexture();
                _overlayDirty = false;
            }

            SyncPlotOutlines();
            HandleZonePainting();
            HandlePlotClick();

            if (_overlayPlane != null && Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            {
                _overlayVisible = !_overlayVisible;
                _overlayPlane.SetActive(_overlayVisible);
            }
        }

        #region Zone tool UI

        private void BuildToolPanel(VisualElement root)
        {
            var toggleButton = new Button { text = "Land Tool" };
            toggleButton.style.position = Position.Absolute;
            toggleButton.style.top = 8;
            toggleButton.style.right = 8;
            toggleButton.clicked += () =>
            {
                _toolActive = !_toolActive;
                _toolPanel.style.display = _toolActive ? DisplayStyle.Flex : DisplayStyle.None;
            };
            root.Add(toggleButton);

            _toolPanel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 40,
                    right = 8,
                    flexDirection = FlexDirection.Column,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f),
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 6,
                    paddingBottom = 6,
                    display = DisplayStyle.None
                }
            };

            foreach (string zoneId in LandModule.ZoneIds)
            {
                AddZoneButton(zoneId, zoneId);
            }

            AddZoneButton(null, "Remove Zone");

            root.Add(_toolPanel);
        }

        private void AddZoneButton(string zoneId, string label)
        {
            var button = new Button { text = label };
            button.clicked += () => _selectedZoneId = zoneId;
            _toolPanel.Add(button);
        }

        #endregion

        #region Painting

        private void HandleZonePainting()
        {
            if (!_toolActive || _camera == null || Mouse.current == null)
            {
                return;
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                _dragging = false;
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame && !UiInputUtility.IsPointerOverUi(_uiRoot, Mouse.current.position.ReadValue()) && TryPickCell(out GridPoint cell))
            {
                _dragging = true;
                _dragStart = cell;
            }

            if (_dragging && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                _dragging = false;

                if (UiInputUtility.IsPointerOverUi(_uiRoot, Mouse.current.position.ReadValue()) || !TryPickCell(out GridPoint end))
                {
                    return; // released over UI (or off the map) - the drag started validly, but don't paint with a UI-covered endpoint
                }

                CellRect rect = RectBetween(_dragStart, end);

                if (_selectedZoneId == null)
                {
                    _land.RequestRemoveZone(rect);
                }
                else
                {
                    _land.RequestSetZone(rect, _selectedZoneId);
                }

                _overlayDirty = true;
            }
        }

        private static CellRect RectBetween(GridPoint a, GridPoint b)
        {
            int minX = Mathf.Min(a.X, b.X);
            int minY = Mathf.Min(a.Y, b.Y);
            int width = Mathf.Abs(a.X - b.X) + 1;
            int height = Mathf.Abs(a.Y - b.Y) + 1;
            return new CellRect(minX, minY, width, height);
        }

        private bool TryPickCell(out GridPoint cell)
        {
            Vector2 screenPos = Mouse.current.position.ReadValue();
            Ray ray = _camera.ScreenPointToRay(screenPos);

            if (Mathf.Approximately(ray.direction.y, 0f))
            {
                cell = default;
                return false;
            }

            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f)
            {
                cell = default;
                return false;
            }

            if (_cachedGrid == null)
            {
                cell = default;
                return false;
            }

            Vector3 hit = ray.origin + ray.direction * t;
            cell = _cachedGrid.WorldToCell(hit.x, hit.z);
            return true;
        }

        private MapGrid _cachedGrid;

        #endregion

        #region Plot click to inspect

        private void HandlePlotClick()
        {
            if (_selectAction == null || !_selectAction.WasPressedThisFrame() || _camera == null || Mouse.current == null || _toolActive)
            {
                return;
            }

            Vector2 screenPos = Mouse.current.position.ReadValue();
            if (UiInputUtility.IsPointerOverUi(_uiRoot, screenPos))
            {
                return;
            }

            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var tag = hit.collider.GetComponent<PlotOutlineTag>();
                if (tag != null)
                {
                    _onPlotSelected?.Invoke(tag.PlotId);
                }
            }
        }

        #endregion

        #region Overlay and outlines

        private void BuildOverlayPlane()
        {
            MapGrid grid = _cachedGrid;
            if (grid == null)
            {
                return;
            }

            float width = (float)(grid.Width * grid.CellSizeMeters);
            float depth = (float)(grid.Depth * grid.CellSizeMeters);

            _overlayPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _overlayPlane.name = "ZoneOverlay";
            Destroy(_overlayPlane.GetComponent<Collider>());
            _overlayPlane.transform.position = new Vector3(width * 0.5f, OverlayHeight, depth * 0.5f);
            _overlayPlane.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _overlayPlane.transform.localScale = new Vector3(width, depth, 1f);
            _overlayPlane.SetActive(_overlayVisible);

            _overlayPlane.GetComponent<MeshRenderer>().sharedMaterial = MakeTransparentMaterial(Color.white);
        }

        /// <summary>URP's Unlit shader needs its blend/queue set by script to actually render transparently; the inspector would normally do this.</summary>
        private static Material MakeTransparentMaterial(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private void RebuildOverlayTexture()
        {
            MapGrid grid = _cachedGrid;
            if (grid == null || _overlayPlane == null)
            {
                return;
            }

            if (_overlayTexture == null)
            {
                _overlayTexture = new Texture2D(grid.Width, grid.Depth, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            }

            var pixels = new Color[grid.Width * grid.Depth];
            CellRect grounds = _land.CrownGrounds;

            for (int y = 0; y < grid.Depth; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = new GridPoint(x, y);
                    Color color = new Color(0f, 0f, 0f, 0f);

                    if (grounds.Contains(cell))
                    {
                        color = new Color(0.9f, 0.85f, 0.3f, 0.35f);
                    }
                    else
                    {
                        string zoneId = _land.GetZoneAt(cell);
                        if (zoneId != null && ZoneColors.TryGetValue(zoneId, out Color zoneColor))
                        {
                            color = zoneColor;
                        }
                    }

                    pixels[y * grid.Width + x] = color;
                }
            }

            _overlayTexture.SetPixels(pixels);
            _overlayTexture.Apply();
            _overlayPlane.GetComponent<MeshRenderer>().sharedMaterial.mainTexture = _overlayTexture;
        }

        private void BuildCrownGroundsOutline()
        {
            MapGrid grid = _cachedGrid;
            if (grid == null)
            {
                return;
            }

            CellRect rect = _land.CrownGrounds;
            _crownGroundsOutline = CreatePlotMarker(rect, new Color(0.9f, 0.85f, 0.2f, 0.6f), "Crown Grounds", null);
        }

        private void SyncPlotOutlines()
        {
            var stale = new List<EntityId>(_plotOutlines.Keys);

            foreach (KeyValuePair<EntityId, PlotState> row in _land.Plots)
            {
                stale.Remove(row.Key);
                if (!_plotOutlines.ContainsKey(row.Key))
                {
                    string ownerName = DescribeOccupant(row.Value);
                    GameObject marker = CreatePlotMarker(row.Value.Rect, new Color(0.9f, 0.3f, 0.3f, 0.5f), ownerName, row.Key);
                    _plotOutlines[row.Key] = marker;
                }
            }

            foreach (EntityId id in stale)
            {
                Destroy(_plotOutlines[id]);
                _plotOutlines.Remove(id);
            }
        }

        private string DescribeOccupant(PlotState plot)
        {
            if (plot.Owner.HasValue)
            {
                return "Owner: " + PersonName(plot.Owner.Value);
            }

            if (plot.Leaseholder.HasValue)
            {
                return "Leased: " + PersonName(plot.Leaseholder.Value);
            }

            return "Crown";
        }

        private string PersonName(EntityId id) =>
            _peopleModule != null && _peopleModule.TryGetPerson(id, out PersonState person) ? person.FullName : id.ToString();

        private GameObject CreatePlotMarker(CellRect rect, Color color, string label, EntityId? plotId)
        {
            MapGrid grid = _cachedGrid;
            double cellSize = grid.CellSizeMeters;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = "PlotOutline";
            marker.transform.position = new Vector3(
                (float)((rect.X + rect.Width * 0.5) * cellSize),
                PlotOutlineHeight,
                (float)((rect.Y + rect.Height * 0.5) * cellSize));
            marker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            marker.transform.localScale = new Vector3((float)(rect.Width * cellSize), (float)(rect.Height * cellSize), 1f);

            marker.GetComponent<MeshRenderer>().sharedMaterial = MakeTransparentMaterial(color);

            if (plotId.HasValue)
            {
                marker.AddComponent<PlotOutlineTag>().PlotId = plotId.Value;
            }
            else
            {
                Destroy(marker.GetComponent<Collider>());
            }

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(marker.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            labelObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = label;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 24;
            textMesh.characterSize = 0.08f;
            textMesh.color = Color.white;

            return marker;
        }

        private sealed class PlotOutlineTag : MonoBehaviour
        {
            public EntityId PlotId;
        }

        #endregion
    }
}
