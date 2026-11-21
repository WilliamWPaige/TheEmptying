using System;
using System.Collections.Generic;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.MapResources;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.MapResources
{
    /// <summary>
    /// Gray-box rendering for trees and rock outcrops: a green cylinder per
    /// standing tree, a gray sphere per outcrop, each labeled with its
    /// remaining stock, click-to-inspect through the same text inspector as
    /// everything else. Reads the module only - nothing here ever changes a
    /// node's stock.
    /// </summary>
    public sealed class MapResourcesPresenter : MonoBehaviour
    {
        private MapResourcesModule _mapResources;
        private MapGrid _grid;
        private Camera _camera;
        private VisualElement _uiRoot;
        private Action<EntityId> _onNodeSelected;

        private readonly Dictionary<EntityId, GameObject> _markers = new Dictionary<EntityId, GameObject>();

        public void Initialize(MapResourcesModule mapResources, MapGrid grid, Camera camera, VisualElement uiRoot, Action<EntityId> onNodeSelected)
        {
            _mapResources = mapResources;
            _grid = grid;
            _camera = camera;
            _uiRoot = uiRoot;
            _onNodeSelected = onNodeSelected;
        }

        private void Update()
        {
            SyncMarkers();
            HandleClick();
        }

        private void SyncMarkers()
        {
            if (_mapResources == null)
            {
                return;
            }

            var stale = new List<EntityId>(_markers.Keys);

            foreach (KeyValuePair<EntityId, ResourceNodeState> row in _mapResources.Nodes)
            {
                stale.Remove(row.Key);
                if (!_markers.TryGetValue(row.Key, out GameObject marker))
                {
                    marker = CreateMarker(row.Key, row.Value);
                    _markers[row.Key] = marker;
                }

                UpdateMarker(marker, row.Value);
            }

            foreach (EntityId id in stale)
            {
                Destroy(_markers[id]);
                _markers.Remove(id);
            }
        }

        private GameObject CreateMarker(EntityId id, ResourceNodeState node)
        {
            PrimitiveType primitive = node.Kind == ResourceNodeKind.Tree ? PrimitiveType.Cylinder : PrimitiveType.Sphere;
            var marker = GameObject.CreatePrimitive(primitive);
            marker.name = $"{(node.Kind == ResourceNodeKind.Tree ? "Tree" : "Outcrop")} {id}";
            marker.AddComponent<ResourceNodeTag>().NodeId = id;

            Color color = node.Kind == ResourceNodeKind.Tree ? new Color(0.25f, 0.55f, 0.25f) : new Color(0.5f, 0.5f, 0.5f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(marker.transform, false);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.LowerCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 24;
            textMesh.characterSize = 0.08f;
            textMesh.color = Color.white;

            return marker;
        }

        private void UpdateMarker(GameObject marker, ResourceNodeState node)
        {
            double cellSize = _grid.CellSizeMeters;
            float height = node.Kind == ResourceNodeKind.Tree ? 3f : 1.2f;
            float footprint = (float)cellSize * 0.7f;

            marker.transform.position = new Vector3(
                (float)((node.Cell.X + 0.5) * cellSize),
                height * 0.5f,
                (float)((node.Cell.Y + 0.5) * cellSize));
            marker.transform.localScale = node.Kind == ResourceNodeKind.Tree
                ? new Vector3(footprint * 0.5f, height * 0.5f, footprint * 0.5f)
                : new Vector3(footprint, height, footprint);

            TextMesh label = marker.GetComponentInChildren<TextMesh>(true);
            if (label != null)
            {
                label.transform.localPosition = new Vector3(0f, node.Kind == ResourceNodeKind.Tree ? 1.2f : 0.8f, 0f);
                label.text = node.RemainingStock.ToString();

                if (_camera != null)
                {
                    BillboardUtility.FaceCameraFlat(label.transform, _camera);
                }
            }
        }

        private void HandleClick()
        {
            if (_camera == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Vector2 screenPos = Mouse.current.position.ReadValue();
            if (_uiRoot != null && UiInputUtility.IsPointerOverUi(_uiRoot, screenPos))
            {
                return;
            }

            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var tag = hit.collider.GetComponent<ResourceNodeTag>();
                if (tag != null)
                {
                    _onNodeSelected?.Invoke(tag.NodeId);
                }
            }
        }

        private sealed class ResourceNodeTag : MonoBehaviour
        {
            public EntityId NodeId;
        }
    }
}
