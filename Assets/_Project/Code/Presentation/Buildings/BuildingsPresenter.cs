using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Buildings
{
    /// <summary>
    /// Gray-box rendering for buildings: a government build menu, a
    /// click-to-place preview (valid/invalid), gray cubes sized to footprint
    /// (height by type), see-through sites with a progress label, and
    /// click-to-inspect. The player never places private buildings. Reads
    /// the module, never writes to it except through the placement API.
    /// </summary>
    public sealed class BuildingsPresenter : MonoBehaviour
    {
        private const float SiteAlpha = 0.35f;

        private BuildingsModule _buildings;
        private ContentDatabase _content;
        private MapGrid _grid;
        private Camera _camera;
        private InputAction _selectAction;
        private Action<EntityId> _onBuildingSelected;
        private VisualElement _uiRoot;
        private Action _onInitialStockpilePlaced;

        private VisualElement _menuPanel;
        private Button _buildMenuToggleButton;
        private string _selectedTypeId;
        private bool _toolActive;
        private bool _placingInitialStockpile;
        private Label _stockpilePrompt;

        private GameObject _previewCube;
        private readonly Dictionary<EntityId, GameObject> _buildingCubes = new Dictionary<EntityId, GameObject>();

        public void Initialize(BuildingsModule buildings, ContentDatabase content, MapGrid grid, Camera camera, InputActionMap gameplayMap, VisualElement uiRoot, Action<EntityId> onBuildingSelected, Action onInitialStockpilePlaced = null)
        {
            _buildings = buildings;
            _content = content;
            _grid = grid;
            _camera = camera;
            _selectAction = gameplayMap.FindAction("Select");
            _onBuildingSelected = onBuildingSelected;
            _uiRoot = uiRoot;
            _onInitialStockpilePlaced = onInitialStockpilePlaced;

            BuildMenu(uiRoot);
            BuildStockpilePrompt(uiRoot);

            if (_buildings.NeedsInitialStockpilePlacement)
            {
                _placingInitialStockpile = true;
                _stockpilePrompt.style.display = DisplayStyle.Flex;
            }
        }

        public void Rebind(BuildingsModule buildings, MapGrid grid)
        {
            _buildings = buildings;
            _grid = grid;

            foreach (GameObject cube in _buildingCubes.Values)
            {
                Destroy(cube);
            }

            _buildingCubes.Clear();
        }

        private void Update()
        {
            SyncBuildingCubes();
            HandlePreviewAndPlacement();
            HandleBuildingClick();
        }

        #region Build menu

        private void BuildStockpilePrompt(VisualElement root)
        {
            _stockpilePrompt = new Label("Place the stockpile: click a spot on the ground for the crown's first storehouse.")
            {
                style =
                {
                    position = Position.Absolute,
                    top = 8,
                    left = 0,
                    right = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = Color.white,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f),
                    paddingTop = 6,
                    paddingBottom = 6,
                    display = DisplayStyle.None
                }
            };

            root.Add(_stockpilePrompt);
        }

        private void BuildMenu(VisualElement root)
        {
            _buildMenuToggleButton = new Button { text = "Build" };
            _buildMenuToggleButton.style.position = Position.Absolute;
            _buildMenuToggleButton.style.top = 8;
            _buildMenuToggleButton.style.right = 96;
            _buildMenuToggleButton.clicked += () =>
            {
                _toolActive = !_toolActive;
                _menuPanel.style.display = _toolActive ? DisplayStyle.Flex : DisplayStyle.None;
                if (!_toolActive)
                {
                    _selectedTypeId = null;
                }
            };
            root.Add(_buildMenuToggleButton);

            _menuPanel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 40,
                    right = 96,
                    flexDirection = FlexDirection.Column,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f),
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 6,
                    paddingBottom = 6,
                    display = DisplayStyle.None
                }
            };

            foreach (ContentEntry entry in _content.ListByType(BuiltInContentTypes.BuildingTypeTypeName))
            {
                var type = (BuildingType)entry.Definition;
                if (!type.IsGovernment)
                {
                    continue;
                }

                var button = new Button { text = type.Name };
                string typeId = type.Id;
                button.clicked += () => _selectedTypeId = typeId;
                _menuPanel.Add(button);
            }

            root.Add(_menuPanel);
        }

        #endregion

        #region Placement

        private void HandlePreviewAndPlacement()
        {
            if (_placingInitialStockpile)
            {
                HandleInitialStockpilePlacement();
                return;
            }

            if (!_toolActive || string.IsNullOrEmpty(_selectedTypeId) || _camera == null || Mouse.current == null || _grid == null)
            {
                HidePreview();
                return;
            }

            if (!TryPickCell(out GridPoint cell))
            {
                HidePreview();
                return;
            }

            ProcessGovernmentPlacementClick(cell, Mouse.current.position.ReadValue(), Mouse.current.leftButton.wasPressedThisFrame, Mouse.current.rightButton.wasPressedThisFrame);
        }

        /// <summary>The actual placement decision, given a cell and a screen position - factored out of HandlePreviewAndPlacement so a test can drive it without touching mouse hardware. Returns the placed building's id, if the click actually placed one.</summary>
        private EntityId? ProcessGovernmentPlacementClick(GridPoint cell, Vector2 screenPos, bool leftClicked, bool rightClicked)
        {
            BuildingType type = FindType(_selectedTypeId);
            if (type == null)
            {
                return null;
            }

            var rect = new CellRect(cell.X, cell.Y, type.FootprintWidth, type.FootprintDepth);
            bool valid = IsPlacementPreviewValid(rect);
            ShowPreview(rect, valid);

            bool clickBlockedByUi = leftClicked && UiInputUtility.IsPointerOverUi(_uiRoot, screenPos);

            EntityId? placedId = null;
            if (valid && leftClicked && !clickBlockedByUi && _buildings.TryPlaceGovernmentBuilding(_selectedTypeId, cell, out EntityId newId, out _))
            {
                placedId = newId;
            }

            if (rightClicked)
            {
                _selectedTypeId = null;
                HidePreview();
            }

            return placedId;
        }

        /// <summary>The player's first action: every other tool is disabled while this is active, and there's nothing to cancel back out of.</summary>
        private void HandleInitialStockpilePlacement()
        {
            if (_camera == null || Mouse.current == null || _grid == null || !TryPickCell(out GridPoint cell))
            {
                HidePreview();
                return;
            }

            BuildingType type = _buildings.GetBuildingType(BuildingsModule.CrownStorehouseTypeId);
            if (type == null)
            {
                return;
            }

            var rect = new CellRect(cell.X, cell.Y, type.FootprintWidth, type.FootprintDepth);
            bool valid = IsPlacementPreviewValid(rect);
            ShowPreview(rect, valid);

            bool clickBlockedByUi = Mouse.current.leftButton.wasPressedThisFrame && UiInputUtility.IsPointerOverUi(_uiRoot, Mouse.current.position.ReadValue());

            if (valid && Mouse.current.leftButton.wasPressedThisFrame && !clickBlockedByUi && _buildings.TryPlaceInitialStockpile(cell, out _, out _))
            {
                _placingInitialStockpile = false;
                _stockpilePrompt.style.display = DisplayStyle.None;
                HidePreview();
                _onInitialStockpilePlaced?.Invoke();
            }
        }

        private bool IsPlacementPreviewValid(CellRect rect)
        {
            foreach (GridPoint cell in rect.Cells())
            {
                if (!_grid.InBounds(cell) || _grid.IsOccupied(cell))
                {
                    return false;
                }
            }

            return true;
        }

        private BuildingType FindType(string typeId)
        {
            foreach (ContentEntry entry in _content.ListByType(BuiltInContentTypes.BuildingTypeTypeName))
            {
                if (entry.Definition.Id == typeId)
                {
                    return (BuildingType)entry.Definition;
                }
            }

            return null;
        }

        private void ShowPreview(CellRect rect, bool valid)
        {
            if (_previewCube == null)
            {
                _previewCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _previewCube.name = "PlacementPreview";
                Destroy(_previewCube.GetComponent<Collider>());
            }

            double cellSize = _grid.CellSizeMeters;
            float height = HeightFor(rect.Width, rect.Height);

            _previewCube.transform.position = new Vector3(
                (float)((rect.X + rect.Width * 0.5) * cellSize),
                height * 0.5f,
                (float)((rect.Y + rect.Height * 0.5) * cellSize));
            _previewCube.transform.localScale = new Vector3((float)(rect.Width * cellSize), height, (float)(rect.Height * cellSize));
            _previewCube.GetComponent<MeshRenderer>().sharedMaterial = MakeTransparentMaterial(valid ? new Color(0.3f, 0.9f, 0.3f, 0.5f) : new Color(0.9f, 0.3f, 0.3f, 0.5f));
            _previewCube.SetActive(true);
        }

        private void HidePreview()
        {
            if (_previewCube != null)
            {
                _previewCube.SetActive(false);
            }
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

            Vector3 hit = ray.origin + ray.direction * t;
            cell = _grid.WorldToCell(hit.x, hit.z);
            return true;
        }

        #endregion

        #region Buildings and sites

        private void SyncBuildingCubes()
        {
            if (_buildings == null)
            {
                return;
            }

            var stale = new List<EntityId>(_buildingCubes.Keys);

            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Buildings)
            {
                stale.Remove(row.Key);
                if (!_buildingCubes.TryGetValue(row.Key, out GameObject cube))
                {
                    cube = CreateBuildingCube(row.Key, row.Value);
                    _buildingCubes[row.Key] = cube;
                }

                UpdateBuildingCube(cube, row.Value);
            }

            foreach (EntityId id in stale)
            {
                Destroy(_buildingCubes[id]);
                _buildingCubes.Remove(id);
            }
        }

        private GameObject CreateBuildingCube(EntityId id, BuildingState building)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Building {id}";
            cube.AddComponent<BuildingTag>().BuildingId = id;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(cube.transform, false);
            labelObject.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.LowerCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 28;
            textMesh.characterSize = 0.08f;
            textMesh.color = Color.white;

            return cube;
        }

        private void UpdateBuildingCube(GameObject cube, BuildingState building)
        {
            double cellSize = _grid.CellSizeMeters;
            BuildingType type = FindType(building.BuildingTypeId);
            float height = type != null ? HeightFor(building.Rect.Width, building.Rect.Height) : 2f;

            cube.transform.position = new Vector3(
                (float)((building.Rect.X + building.Rect.Width * 0.5) * cellSize),
                height * 0.5f,
                (float)((building.Rect.Y + building.Rect.Height * 0.5) * cellSize));
            cube.transform.localScale = new Vector3((float)(building.Rect.Width * cellSize), height, (float)(building.Rect.Height * cellSize));

            bool isSite = building.Construction != null;
            Color color = isSite ? new Color(0.6f, 0.6f, 0.6f, SiteAlpha) : new Color(0.55f, 0.55f, 0.55f, 1f);
            cube.GetComponent<MeshRenderer>().sharedMaterial = isSite ? MakeTransparentMaterial(color) : MakeOpaqueMaterial(color);

            TextMesh label = cube.GetComponentInChildren<TextMesh>(true);
            if (label != null)
            {
                label.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                label.text = isSite ? $"{ConstructionProgressPercent(building, type):F0}%" : "";
                label.gameObject.SetActive(isSite);

                if (isSite && _camera != null)
                {
                    BillboardUtility.FaceCameraUpright(label.transform, _camera);
                }
            }
        }

        private static float ConstructionProgressPercent(BuildingState building, BuildingType type)
        {
            if (type == null || building.Construction == null)
            {
                return 100f;
            }

            double totalGoods = 0;
            double deliveredGoods = 0;
            foreach (GoodAmount cost in type.ConstructionCost)
            {
                totalGoods += cost.Amount;
                deliveredGoods += Mathf.Min(cost.Amount, building.Construction.DeliveredGoods.TryGetValue(cost.Good.Id, out int d) ? d : 0);
            }

            return totalGoods > 0 ? (float)(deliveredGoods / totalGoods * 100.0) : 100f;
        }

        private static float HeightFor(int width, int depth) => Mathf.Clamp(Mathf.Max(width, depth) * 0.5f, 2f, 8f);

        #endregion

        #region Click to inspect

        private void HandleBuildingClick()
        {
            if (_selectAction == null || !_selectAction.WasPressedThisFrame() || _camera == null || Mouse.current == null || _toolActive || _placingInitialStockpile)
            {
                return;
            }

            TryHandleBuildingClickAt(Mouse.current.position.ReadValue());
        }

        /// <summary>The actual click-to-inspect decision, given a screen position - factored out of HandleBuildingClick so a test can drive it without touching mouse hardware.</summary>
        private void TryHandleBuildingClickAt(Vector2 screenPos)
        {
            if (UiInputUtility.IsPointerOverUi(_uiRoot, screenPos))
            {
                return;
            }

            // A cube just created or moved this same frame (SyncBuildingCubes, above) doesn't update
            // the physics engine's own broadphase data until the next sync point - without forcing it
            // here, a click on a building right after it's placed can silently miss.
            Physics.SyncTransforms();

            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var tag = hit.collider.GetComponent<BuildingTag>();
                if (tag != null)
                {
                    _onBuildingSelected?.Invoke(tag.BuildingId);
                }
            }
        }

        #region Testing

        /// <summary>For tests: places the stockpile directly, with the same side effects a successful click would have (clearing the prompt, resuming play) - so a test that isn't about the player's first action doesn't have to simulate that click too.</summary>
        public bool PlaceInitialStockpileForTesting(GridPoint cell)
        {
            if (!_buildings.TryPlaceInitialStockpile(cell, out _, out _))
            {
                return false;
            }

            _placingInitialStockpile = false;
            _stockpilePrompt.style.display = DisplayStyle.None;
            HidePreview();
            _onInitialStockpilePlaced?.Invoke();
            return true;
        }

        /// <summary>For tests: the build menu's own toggle button, as a stand-in for a real UI screen position (this project's PanelSettings use a 1:1 pixel scale, so panel space and screen space coincide).</summary>
        public Vector2 BuildMenuToggleButtonPositionForTesting => _buildMenuToggleButton.worldBound.center;

        /// <summary>For tests: whether a click at this screen position would currently be swallowed by UI instead of reaching the 3D world.</summary>
        public bool IsPointerOverUiForTesting(Vector2 screenPosition) => UiInputUtility.IsPointerOverUi(_uiRoot, screenPosition);

        /// <summary>For tests: the build menu's own toggle button's resolved world-space (panel-space) rect, for debugging coordinate conversions.</summary>
        public Rect BuildMenuToggleButtonWorldBoundForTesting => _buildMenuToggleButton.worldBound;

        /// <summary>
        /// For tests: the screen position that maps to this panel-space position, found by bisection
        /// rather than assumed from Screen.width/height - UI Toolkit's runtime panel can run at a
        /// different scale (DPI, reference resolution) than the raw screen size, and only the
        /// screen-to-panel direction (RuntimePanelUtils.ScreenToPanel, used by the real click path) is
        /// exposed, so this inverts it numerically instead of guessing the scale factor.
        /// </summary>
        public Vector2 ScreenPositionForPanelPointForTesting(Vector2 panelPosition)
        {
            float xLo = -Screen.width, xHi = Screen.width * 2f;
            float yLo = -Screen.height, yHi = Screen.height * 2f;

            // Each axis might map increasing or decreasing (a Y-flip is typical; X usually isn't,
            // but this doesn't assume that) - probe the direction once before bisecting on it.
            bool xIncreasing = RuntimePanelUtils.ScreenToPanel(_uiRoot.panel, new Vector2(xHi, 0f)).x
                > RuntimePanelUtils.ScreenToPanel(_uiRoot.panel, new Vector2(xLo, 0f)).x;
            bool yIncreasing = RuntimePanelUtils.ScreenToPanel(_uiRoot.panel, new Vector2(0f, yHi)).y
                > RuntimePanelUtils.ScreenToPanel(_uiRoot.panel, new Vector2(0f, yLo)).y;

            for (int i = 0; i < 40; i++)
            {
                float xMid = (xLo + xHi) * 0.5f;
                float panelX = RuntimePanelUtils.ScreenToPanel(_uiRoot.panel, new Vector2(xMid, 0f)).x;
                if ((panelX < panelPosition.x) == xIncreasing) { xLo = xMid; } else { xHi = xMid; }

                float yMid = (yLo + yHi) * 0.5f;
                float panelY = RuntimePanelUtils.ScreenToPanel(_uiRoot.panel, new Vector2(0f, yMid)).y;
                if ((panelY < panelPosition.y) == yIncreasing) { yLo = yMid; } else { yHi = yMid; }
            }

            return new Vector2((xLo + xHi) * 0.5f, (yLo + yHi) * 0.5f);
        }

        /// <summary>For tests: runs the exact guarded placement decision a real left-click at screenPosition would, with typeId selected and the tool active. Returns the placed building's id, if any.</summary>
        public EntityId? SimulatePlacementClickForTesting(string typeId, GridPoint cell, Vector2 screenPosition)
        {
            _toolActive = true;
            _selectedTypeId = typeId;
            return ProcessGovernmentPlacementClick(cell, screenPosition, leftClicked: true, rightClicked: false);
        }

        /// <summary>For tests: closes the build menu/tool, exactly as clicking its toggle button a second time would.</summary>
        public void CloseBuildMenuForTesting()
        {
            _toolActive = false;
            _selectedTypeId = null;
            _menuPanel.style.display = DisplayStyle.None;
            HidePreview(); // the ghost preview cube stays parked at the last-aimed cell otherwise, intercepting any raycast there
        }

        /// <summary>For tests: raycasts straight down through a world XZ position, bypassing the camera entirely, for debugging.</summary>
        public string DescribeStraightDownRaycastForTesting(float worldX, float worldZ)
        {
            var origin = new Vector3(worldX, 100f, worldZ);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f))
            {
                return $"no hit; origin={origin}";
            }

            var tag = hit.collider.GetComponent<BuildingTag>();
            return $"hit {hit.collider.gameObject.name} at {hit.point}, hasBuildingTag={tag != null}";
        }

        /// <summary>For tests: lists every tracked building cube's id, position, scale and active state, for debugging.</summary>
        public string DescribeAllBuildingCubesForTesting()
        {
            var lines = new List<string>();
            foreach (KeyValuePair<EntityId, GameObject> entry in _buildingCubes)
            {
                GameObject cube = entry.Value;
                Collider collider = cube.GetComponent<Collider>();
                lines.Add($"{entry.Key}: pos={cube.transform.position} scale={cube.transform.localScale} activeSelf={cube.activeSelf} activeInHierarchy={cube.activeInHierarchy} hasCollider={collider != null} colliderEnabled={collider != null && collider.enabled}");
            }

            return lines.Count == 0 ? "no cubes tracked" : string.Join(" ||| ", lines);
        }

        /// <summary>For tests: describes whatever a raycast from screenPosition would hit, for debugging.</summary>
        public string DescribeRaycastForTesting(Vector2 screenPosition)
        {
            if (_camera == null)
            {
                return "no camera";
            }

            Ray ray = _camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                return $"no hit; ray origin={ray.origin} dir={ray.direction}";
            }

            var tag = hit.collider.GetComponent<BuildingTag>();
            return $"hit {hit.collider.gameObject.name} at {hit.point}, distance={hit.distance}, hasBuildingTag={tag != null}";
        }

        /// <summary>For tests: runs the exact guarded click-to-inspect decision a real left-click at screenPosition would.</summary>
        public void ClickAtForTesting(Vector2 screenPosition)
        {
            if (_toolActive || _placingInitialStockpile)
            {
                return;
            }

            TryHandleBuildingClickAt(screenPosition);
        }

        #endregion

        #endregion

        #region Materials

        private static Material MakeOpaqueMaterial(Color color) => new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };

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

        #endregion

        private sealed class BuildingTag : MonoBehaviour
        {
            public EntityId BuildingId;
        }
    }
}
