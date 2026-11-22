using TheEmptying.Modules.Map;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// R6 test helper: highlights the ground cell under the mouse and
    /// shows its world position and chunk in a small corner label. Hidden
    /// while the pointer is over any UI.
    /// </summary>
    public sealed class MapCursorPresenter : MonoBehaviour
    {
        private MapModule _mapModule;
        private FloatingOrigin _floatingOrigin;
        private Camera _camera;
        private VisualElement _uiRoot;

        private GameObject _highlight;
        private Label _label;

        public void Initialize(MapModule mapModule, FloatingOrigin floatingOrigin, Camera camera, VisualElement uiRoot)
        {
            _mapModule = mapModule;
            _floatingOrigin = floatingOrigin;
            _camera = camera;
            _uiRoot = uiRoot;

            BuildHighlightQuad();
            BuildLabel(uiRoot);
        }

        private void BuildHighlightQuad()
        {
            _highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _highlight.name = "Cell Highlight (test helper)";

            Collider highlightCollider = _highlight.GetComponent<Collider>();
            if (highlightCollider != null)
            {
                Destroy(highlightCollider);
            }

            _highlight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "CellHighlightMaterial", color = new Color(1f, 0.85f, 0.2f, 1f) };

            var renderer = _highlight.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _highlight.SetActive(false);
        }

        private void BuildLabel(VisualElement uiRoot)
        {
            _label = new Label
            {
                style =
                {
                    position = Position.Absolute,
                    left = 8,
                    bottom = 8,
                    color = Color.white,
                    backgroundColor = new Color(0f, 0f, 0f, 0.5f),
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 2,
                    paddingBottom = 2,
                    display = DisplayStyle.None
                }
            };
            uiRoot.Add(_label);
        }

        private void Update()
        {
            if (_mapModule == null || _camera == null || Mouse.current == null)
            {
                return;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            if (UiInputUtility.IsPointerOverUi(_uiRoot, mousePosition))
            {
                SetVisible(false);
                return;
            }

            Ray ray = _camera.ScreenPointToRay(mousePosition);

            // The ground is always y = 0 in both world and render space: the floating origin's offset.y never
            // moves, since the camera's own world y is never shifted either (see FloatingOrigin remarks).
            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float distance))
            {
                SetVisible(false);
                return;
            }

            Vector3 renderHit = ray.GetPoint(distance);
            Vector3 worldHit = _floatingOrigin.RenderToWorld(renderHit);

            double cellSize = _mapModule.CellSizeMeters;
            CellCoord cell = _mapModule.WorldToCell(worldHit.x, worldHit.z);
            ChunkCoord chunk = _mapModule.CellToChunk(cell);

            var cellOriginWorld = new Vector3((float)(cell.X * cellSize), 0f, (float)(cell.Z * cellSize));
            Vector3 cellCenterRender = _floatingOrigin.WorldToRender(cellOriginWorld) + new Vector3((float)cellSize * 0.5f, 0.05f, (float)cellSize * 0.5f);

            _highlight.transform.position = cellCenterRender;
            _highlight.transform.localScale = new Vector3((float)cellSize, (float)cellSize, 1f);
            SetVisible(true);

            _label.text = $"Cell ({cell.X}, {cell.Z})   World ({worldHit.x:0.#}, {worldHit.z:0.#})   Chunk ({chunk.X}, {chunk.Z})";
        }

        private void SetVisible(bool visible)
        {
            if (_highlight.activeSelf != visible)
            {
                _highlight.SetActive(visible);
            }

            _label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
