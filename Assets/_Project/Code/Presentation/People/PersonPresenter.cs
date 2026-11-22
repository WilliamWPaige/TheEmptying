using System.Collections.Generic;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Presentation.Content;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.People
{
    /// <summary>
    /// R7: draws each living person with its type's model, through the
    /// floating origin, only while their chunk is loaded. Clicking a
    /// person selects them (a simple highlight plus a small label with
    /// their Id and cell); clicking anything else (empty ground, a tree,
    /// a stone) clears the selection. Clicks on UI never select.
    /// </summary>
    public sealed class PersonPresenter : MonoBehaviour
    {
        private PeopleModule _peopleModule;
        private MapModule _mapModule;
        private FloatingOrigin _floatingOrigin;
        private WorldMapView _worldMapView;
        private Camera _camera;
        private VisualElement _uiRoot;

        private readonly Dictionary<string, UnitAsset> _unitAssetsById = new Dictionary<string, UnitAsset>();
        private readonly Dictionary<EntityId, GameObject> _activeInstances = new Dictionary<EntityId, GameObject>();
        private readonly Dictionary<GameObject, EntityId> _entityByInstance = new Dictionary<GameObject, EntityId>();
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();
        private readonly List<EntityId> _toHide = new List<EntityId>();

        private Transform _root;
        private GameObject _highlight;
        private Label _label;

        public EntityId? SelectedId { get; private set; }

        public void Initialize(PeopleModule peopleModule, MapModule mapModule, FloatingOrigin floatingOrigin, WorldMapView worldMapView, Camera camera, VisualElement uiRoot)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;
            _floatingOrigin = floatingOrigin;
            _worldMapView = worldMapView;
            _camera = camera;
            _uiRoot = uiRoot;

            _root = new GameObject("People").transform;

            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            if (catalog != null)
            {
                foreach (UnitAsset asset in catalog.Units)
                {
                    if (asset != null)
                    {
                        _unitAssetsById[asset.Id] = asset;
                    }
                }
            }

            BuildHighlight();
            BuildLabel();
        }

        /// <summary>After a load, the simulation (and its modules) are new instances; every instance goes back to the pool and the selection is cleared, since the old EntityIds no longer mean anything.</summary>
        public void Rebind(PeopleModule peopleModule, MapModule mapModule)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;
            ClearSelection();

            foreach (GameObject instance in _activeInstances.Values)
            {
                instance.SetActive(false);
                _pool.Push(instance);
            }

            _activeInstances.Clear();
            _entityByInstance.Clear();
        }

        public void ClearSelection()
        {
            SelectedId = null;
            _highlight.SetActive(false);
            _label.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (_peopleModule == null)
            {
                return;
            }

            UpdateInstances();
            HandleClick();
            UpdateSelectionDisplay();
        }

        private void UpdateInstances()
        {
            var seen = new HashSet<EntityId>();

            foreach (KeyValuePair<EntityId, PersonState> row in _peopleModule.People)
            {
                EntityId id = row.Key;
                PersonState person = row.Value;
                seen.Add(id);

                ChunkCoord chunk = _mapModule.WorldToChunk(person.WorldX, person.WorldZ);
                if (!_worldMapView.IsChunkLoaded(chunk))
                {
                    HideInstance(id);
                    continue;
                }

                if (!_activeInstances.TryGetValue(id, out GameObject instance))
                {
                    if (!_unitAssetsById.TryGetValue(person.UnitContentId, out UnitAsset asset) || asset.Model == null)
                    {
                        continue;
                    }

                    instance = _pool.Count > 0 ? _pool.Pop() : Object.Instantiate(asset.Model, _root);
                    instance.SetActive(true);
                    _activeInstances[id] = instance;
                    _entityByInstance[instance] = id;
                }

                var worldPosition = new Vector3((float)person.WorldX, 0f, (float)person.WorldZ);
                instance.transform.position = _floatingOrigin.WorldToRender(worldPosition);
            }

            // Anyone no longer alive (removed or destroyed) also goes back to the pool.
            _toHide.Clear();
            foreach (EntityId id in _activeInstances.Keys)
            {
                if (!seen.Contains(id))
                {
                    _toHide.Add(id);
                }
            }

            foreach (EntityId id in _toHide)
            {
                HideInstance(id);
            }
        }

        private void HideInstance(EntityId id)
        {
            if (!_activeInstances.TryGetValue(id, out GameObject instance))
            {
                return;
            }

            instance.SetActive(false);
            _activeInstances.Remove(id);
            _entityByInstance.Remove(instance);
            _pool.Push(instance);

            if (SelectedId == id)
            {
                ClearSelection();
            }
        }

        private void HandleClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            if (UiInputUtility.IsPointerOverUi(_uiRoot, mousePosition))
            {
                return;
            }

            Ray ray = _camera.ScreenPointToRay(mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit) && _entityByInstance.TryGetValue(hit.collider.gameObject, out EntityId id))
            {
                SelectedId = id;
            }
            else
            {
                ClearSelection();
            }
        }

        private void UpdateSelectionDisplay()
        {
            if (!SelectedId.HasValue || !_peopleModule.TryGetPerson(SelectedId.Value, out PersonState person))
            {
                return;
            }

            CellCoord cell = _mapModule.WorldToCell(person.WorldX, person.WorldZ);
            var worldPosition = new Vector3((float)person.WorldX, 0.05f, (float)person.WorldZ);

            _highlight.transform.position = _floatingOrigin.WorldToRender(worldPosition);
            _highlight.SetActive(true);

            _label.style.display = DisplayStyle.Flex;
            _label.text = $"Selected: {SelectedId.Value} at cell ({cell.X}, {cell.Z})";
        }

        private void BuildHighlight()
        {
            _highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _highlight.name = "Person Selection Highlight";

            Collider highlightCollider = _highlight.GetComponent<Collider>();
            if (highlightCollider != null)
            {
                Destroy(highlightCollider);
            }

            _highlight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _highlight.transform.localScale = new Vector3(1.5f, 1.5f, 1f);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "PersonSelectionMaterial", color = new Color(0.2f, 0.7f, 1f, 1f) };

            var renderer = _highlight.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _highlight.SetActive(false);
        }

        private void BuildLabel()
        {
            _label = new Label
            {
                style =
                {
                    position = Position.Absolute,
                    left = 8,
                    top = 8,
                    color = Color.white,
                    backgroundColor = new Color(0f, 0f, 0f, 0.5f),
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 2,
                    paddingBottom = 2,
                    display = DisplayStyle.None
                }
            };
            _uiRoot.Add(_label);
        }
    }
}
