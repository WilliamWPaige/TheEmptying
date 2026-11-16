using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
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

        private VisualElement _menuPanel;
        private string _selectedTypeId;
        private bool _toolActive;

        private GameObject _previewCube;
        private readonly Dictionary<EntityId, GameObject> _buildingCubes = new Dictionary<EntityId, GameObject>();

        public void Initialize(BuildingsModule buildings, ContentDatabase content, MapGrid grid, Camera camera, InputActionMap gameplayMap, VisualElement uiRoot, Action<EntityId> onBuildingSelected)
        {
            _buildings = buildings;
            _content = content;
            _grid = grid;
            _camera = camera;
            _selectAction = gameplayMap.FindAction("Select");
            _onBuildingSelected = onBuildingSelected;

            BuildMenu(uiRoot);
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

        private void BuildMenu(VisualElement root)
        {
            var toggleButton = new Button { text = "Build" };
            toggleButton.style.position = Position.Absolute;
            toggleButton.style.top = 8;
            toggleButton.style.right = 96;
            toggleButton.clicked += () =>
            {
                _toolActive = !_toolActive;
                _menuPanel.style.display = _toolActive ? DisplayStyle.Flex : DisplayStyle.None;
                if (!_toolActive)
                {
                    _selectedTypeId = null;
                }
            };
            root.Add(toggleButton);

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

            BuildingType type = FindType(_selectedTypeId);
            if (type == null)
            {
                return;
            }

            var rect = new CellRect(cell.X, cell.Y, type.FootprintWidth, type.FootprintDepth);
            bool valid = IsPlacementPreviewValid(rect);
            ShowPreview(rect, valid);

            if (valid && Mouse.current.leftButton.wasPressedThisFrame)
            {
                _buildings.TryPlaceGovernmentBuilding(_selectedTypeId, cell, out _, out _);
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                _selectedTypeId = null;
                HidePreview();
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
            if (_selectAction == null || !_selectAction.WasPressedThisFrame() || _camera == null || Mouse.current == null || _toolActive)
            {
                return;
            }

            Vector2 screenPos = Mouse.current.position.ReadValue();
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
