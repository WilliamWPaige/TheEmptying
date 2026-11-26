using System;
using TheEmptying.Core.Content;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Ponds;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.Content;
using TheEmptying.Presentation.People;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using BuildingPresenter = TheEmptying.Presentation.Buildings.BuildingPresenter;
using ZonePresenter = TheEmptying.Presentation.Zones.ZonePresenter;
using ZoningMenuPresenter = TheEmptying.Presentation.Zones.ZoningMenuPresenter;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>
    /// R8: the single click router for the map - a person, a resource or
    /// (R11) a building clicked becomes the selection, shown in the
    /// docked info window with a highlight; clicking anything else (or
    /// losing the thing that was selected) clears it. Clicks on UI never
    /// reach this. R11 also checks the starting site marker first, while
    /// it still exists.
    /// </summary>
    public sealed class WorldSelectionPresenter : MonoBehaviour
    {
        private PeopleModule _peopleModule;
        private MapModule _mapModule;
        private BuildingsModule _buildingsModule;
        private ZonesModule _zonesModule;
        private PondsModule _pondsModule;
        private ContentDatabase _content;
        private FloatingOrigin _floatingOrigin;
        private StrategyCameraController _cameraController;
        private PersonPresenter _personPresenter;
        private BuildingPresenter _buildingPresenter;
        private ZonePresenter _zonePresenter;
        private TemporaryChunkDecorations _decorations;
        private PondsPresenter _pondsPresenter;
        private Camera _camera;
        private VisualElement _uiRoot;

        private readonly InfoWindowPresenter _infoWindow = new InfoWindowPresenter();
        private ISelectionTarget _current;
        private GameObject _highlight;

        /// <summary>R9: what was selected right before the Escape menu force-hid this window, for ForceReopenAfterModal to restore; null when nothing was selected at the time.</summary>
        private ISelectionTarget _hiddenByModal;

        /// <summary>The selected person's Id, for the People cheat's "remove selected person"; null if nothing is selected or the selection isn't a person.</summary>
        public EntityId? SelectedPersonId => _current is PersonSelectionTarget person ? person.Id : (EntityId?)null;

        /// <summary>R12: the selected zone's Id, for the Stockpile cheat's "add to selected stockpile"; null if nothing is selected or the selection isn't a zone.</summary>
        public EntityId? SelectedZoneId => _current is ZoneSelectionTarget zone ? zone.Id : (EntityId?)null;

        /// <summary>R17: the selected pond's Id, for the Resources cheat's "Empty pond"/"Fill pond"; null if nothing is selected or the selection isn't a pond.</summary>
        public EntityId? SelectedPondId => _current is PondSelectionTarget pond ? pond.Id : (EntityId?)null;

        public bool IsOpen => _infoWindow.IsOpen;

        /// <summary>R11: the starting site's marker, while it still exists (set by GameBootstrap; cleared once the kingdom is founded, since it's gone for good).</summary>
        public StartingSiteMarkerPresenter SiteMarker { get; set; }

        /// <summary>R18: set by GameBootstrap right after both are built. While the zoning menu is open, world clicks are ignored the same way - it hides this window's selection entirely anyway, but this also stops a click from quietly re-selecting something underneath it.</summary>
        public ZoningMenuPresenter ZoningMenu { get; set; }

        /// <summary>R11: called when the starting site marker is clicked - GameBootstrap opens the founding popup.</summary>
        public Action OnSiteMarkerClicked { get; set; }

        public void Initialize(
            PeopleModule peopleModule,
            MapModule mapModule,
            BuildingsModule buildingsModule,
            ZonesModule zonesModule,
            PondsModule pondsModule,
            ContentDatabase content,
            FloatingOrigin floatingOrigin,
            StrategyCameraController cameraController,
            PersonPresenter personPresenter,
            BuildingPresenter buildingPresenter,
            ZonePresenter zonePresenter,
            TemporaryChunkDecorations decorations,
            PondsPresenter pondsPresenter,
            Camera camera,
            VisualElement uiRoot)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;
            _buildingsModule = buildingsModule;
            _zonesModule = zonesModule;
            _pondsModule = pondsModule;
            _content = content;
            _floatingOrigin = floatingOrigin;
            _cameraController = cameraController;
            _personPresenter = personPresenter;
            _buildingPresenter = buildingPresenter;
            _zonePresenter = zonePresenter;
            _decorations = decorations;
            _pondsPresenter = pondsPresenter;
            _camera = camera;
            _uiRoot = uiRoot;

            _infoWindow.Initialize(uiRoot, CenterCameraOnSelection, ClearSelection);
            BuildHighlight();
        }

        /// <summary>After a load, the simulation (and its modules) are new instances; the old selection no longer means anything.</summary>
        public void Rebind(PeopleModule peopleModule, MapModule mapModule, BuildingsModule buildingsModule, ZonesModule zonesModule, PondsModule pondsModule, ContentDatabase content)
        {
            _peopleModule = peopleModule;
            _mapModule = mapModule;
            _buildingsModule = buildingsModule;
            _zonesModule = zonesModule;
            _pondsModule = pondsModule;
            _content = content;
            ClearSelection();
        }

        public void ClearSelection()
        {
            _current = null;
            _infoWindow.Hide();
            _highlight.SetActive(false);
            _zonePresenter?.SetSelected(null);
        }

        /// <summary>Escape's entry point for the info window (fits between the debug window and the Escape menu in GameBootstrap's priority order).</summary>
        public void HandleEscapePressed()
        {
            ClearSelection();
        }

        /// <summary>R9: the Escape menu is modal - it hides this window while open, remembering the selection so Close() can bring it back.</summary>
        public void ForceCloseForModal()
        {
            _hiddenByModal = _current;
            if (_current != null)
            {
                ClearSelection();
            }
        }

        /// <summary>R9: undoes ForceCloseForModal once the Escape menu closes; a no-op if nothing was selected at the time.</summary>
        public void ForceReopenAfterModal()
        {
            if (_hiddenByModal == null)
            {
                return;
            }

            Select(_hiddenByModal);
            _hiddenByModal = null;
        }

        private void Update()
        {
            if (_mapModule == null)
            {
                return;
            }

            HandleClick();

            if (_current == null)
            {
                return;
            }

            if (!_current.TryGetWorldPosition(out Vector3 worldPosition))
            {
                ClearSelection();
                return;
            }

            if (!(_current is ZoneSelectionTarget))
            {
                PositionHighlight(worldPosition, _current.Footprint);
            }

            _infoWindow.Refresh(_current);
        }

        private void HandleClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (ZoningMenu != null && ZoningMenu.IsOpen)
            {
                return;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            if (UiInputUtility.IsPointerOverUi(_uiRoot, mousePosition))
            {
                return;
            }

            Ray ray = _camera.ScreenPointToRay(mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (SiteMarker != null && SiteMarker.IsPart(hit.collider.gameObject))
                {
                    OnSiteMarkerClicked?.Invoke();
                    return;
                }

                if (_personPresenter.TryGetEntityForInstance(hit.collider.gameObject, out EntityId personId))
                {
                    Select(new PersonSelectionTarget(_peopleModule, _content, personId));
                    return;
                }

                if (_decorations.TryGetPlacement(hit.collider.gameObject, out ChunkPlacement placement))
                {
                    ResourceAsset asset = _decorations.GetAsset(placement.Kind);
                    if (asset != null)
                    {
                        Select(new ResourceSelectionTarget(_mapModule, _content, asset, placement));
                        return;
                    }
                }

                if (_pondsPresenter != null && _pondsPresenter.TryGetPondForInstance(hit.collider.gameObject, out EntityId pondId))
                {
                    Select(new PondSelectionTarget(_pondsModule, pondId));
                    return;
                }

                if (_buildingPresenter != null && _buildingPresenter.TryGetBuildingForInstance(hit.collider.gameObject, out EntityId buildingId))
                {
                    Select(new BuildingSelectionTarget(_buildingsModule, _peopleModule, _content, buildingId));
                    return;
                }

                if (_zonePresenter != null && _zonePresenter.TryGetZoneForInstance(hit.collider.gameObject, out EntityId zoneId))
                {
                    Select(new ZoneSelectionTarget(_zonesModule, _peopleModule, _content, _mapModule, zoneId, HandleZoneRemoved));
                    return;
                }
            }

            ClearSelection();
        }

        /// <summary>R12: a zone's "Remove zone" button - the zone is already gone from ZonesModule; redraw the ground so its visual disappears too, then clear the selection.</summary>
        private void HandleZoneRemoved()
        {
            _zonePresenter?.Rebuild(_zonesModule);
            ClearSelection();
        }

        private void Select(ISelectionTarget target)
        {
            _current = target;
            _infoWindow.Show(target);

            if (target is ZoneSelectionTarget zoneTarget)
            {
                _highlight.SetActive(false);
                _zonePresenter?.SetSelected(zoneTarget.Id);
                return;
            }

            _zonePresenter?.SetSelected(null);
            if (target.TryGetWorldPosition(out Vector3 worldPosition))
            {
                PositionHighlight(worldPosition, target.Footprint);
            }
        }

        private void CenterCameraOnSelection()
        {
            if (_current != null && _current.TryGetWorldPosition(out Vector3 worldPosition))
            {
                _cameraController.SetFocusPoint(worldPosition.x, worldPosition.z);
            }
        }

        private void PositionHighlight(Vector3 worldPosition, Vector2 footprint)
        {
            Vector3 renderPosition = _floatingOrigin.WorldToRender(worldPosition) + new Vector3(0f, 0.05f, 0f);
            _highlight.transform.position = renderPosition;
            _highlight.transform.localScale = new Vector3(footprint.x, footprint.y, 1f);
            _highlight.SetActive(true);
        }

        private void BuildHighlight()
        {
            _highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _highlight.name = "Selection Highlight";

            Collider highlightCollider = _highlight.GetComponent<Collider>();
            if (highlightCollider != null)
            {
                Destroy(highlightCollider);
            }

            _highlight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "SelectionHighlightMaterial", color = new Color(0.2f, 0.7f, 1f, 1f) };

            var renderer = _highlight.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _highlight.SetActive(false);
        }
    }
}
