using System;
using System.Collections.Generic;
using TheEmptying.Modules.MapMovement;
using UnityEngine;
using UnityEngine.InputSystem;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.MapMovement
{
    /// <summary>
    /// Gray-box rendering for the map and its movers: a flat ground plane
    /// with grid lines, a capsule per mover that moves smoothly between
    /// ticks, and click-to-inspect. Pure presentation: reads the module,
    /// never writes to it.
    /// </summary>
    public sealed class MapMovementPresenter : MonoBehaviour
    {
        private const float CapsuleHeight = 2f;

        private MapMovementModule _module;
        private Camera _camera;
        private InputAction _selectAction;
        private Action<EntityId> _onCapsuleSelected;

        private readonly Dictionary<EntityId, GameObject> _capsules = new Dictionary<EntityId, GameObject>();
        private readonly Dictionary<EntityId, Vector3> _previousPositions = new Dictionary<EntityId, Vector3>();
        private readonly Dictionary<EntityId, Vector3> _currentPositions = new Dictionary<EntityId, Vector3>();
        private readonly List<EntityId> _staleIds = new List<EntityId>();

        private float _tickProgress01;

        public void Initialize(MapMovementModule module, Camera camera, InputActionMap gameplayMap, Action<EntityId> onCapsuleSelected)
        {
            _module = module;
            _camera = camera;
            _selectAction = gameplayMap.FindAction("Select");
            _onCapsuleSelected = onCapsuleSelected;

            BuildGround();
        }

        /// <summary>After a load swaps in a new Simulation (and a new module instance), drop every stale capsule and start tracking the new module's movers fresh.</summary>
        public void Rebind(MapMovementModule module)
        {
            _module = module;

            foreach (GameObject capsule in _capsules.Values)
            {
                Destroy(capsule);
            }

            _capsules.Clear();
            _previousPositions.Clear();
            _currentPositions.Clear();
        }

        /// <summary>Called once per real tick stepped, right after Simulation.Step(), so interpolation always blends between the two most recent tick positions.</summary>
        public void OnTickAdvanced()
        {
            foreach (KeyValuePair<EntityId, MoverState> row in _module.Movers)
            {
                var worldPos = ToUnityPosition(row.Value.X, row.Value.Y);
                _previousPositions[row.Key] = _currentPositions.TryGetValue(row.Key, out Vector3 existing) ? existing : worldPos;
                _currentPositions[row.Key] = worldPos;
            }
        }

        /// <summary>The fraction of the way from the last stepped tick to the next one, for smooth interpolation between ticks.</summary>
        public void SetTickProgress(float progress01)
        {
            _tickProgress01 = Mathf.Clamp01(progress01);
        }

        private void Update()
        {
            SyncCapsules();
            HandleSelection();
        }

        private void SyncCapsules()
        {
            _staleIds.Clear();
            foreach (EntityId id in _capsules.Keys)
            {
                if (!_currentPositions.ContainsKey(id))
                {
                    _staleIds.Add(id);
                }
            }

            foreach (EntityId id in _staleIds)
            {
                Destroy(_capsules[id]);
                _capsules.Remove(id);
                _previousPositions.Remove(id);
                _currentPositions.Remove(id);
            }

            foreach (KeyValuePair<EntityId, Vector3> entry in _currentPositions)
            {
                if (!_capsules.TryGetValue(entry.Key, out GameObject capsule))
                {
                    capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    capsule.name = $"Mover {entry.Key}";
                    capsule.transform.localScale = new Vector3(0.8f, CapsuleHeight * 0.5f, 0.8f);
                    Destroy(capsule.GetComponent<Collider>());
                    var collider = capsule.AddComponent<CapsuleCollider>();
                    collider.height = 2f;
                    capsule.AddComponent<MoverCapsuleTag>().EntityId = entry.Key;
                    _capsules[entry.Key] = capsule;
                }

                Vector3 previous = _previousPositions.TryGetValue(entry.Key, out Vector3 p) ? p : entry.Value;
                capsule.transform.position = Vector3.Lerp(previous, entry.Value, _tickProgress01);
            }
        }

        private void HandleSelection()
        {
            if (_selectAction == null || !_selectAction.WasPressedThisFrame() || _camera == null || Mouse.current == null)
            {
                return;
            }

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Ray ray = _camera.ScreenPointToRay(screenPos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var tag = hit.collider.GetComponent<MoverCapsuleTag>();
                if (tag != null)
                {
                    _onCapsuleSelected?.Invoke(tag.EntityId);
                }
            }
        }

        private void BuildGround()
        {
            MapGrid grid = _module.Grid;
            float width = (float)(grid.Width * grid.CellSizeMeters);
            float depth = (float)(grid.Depth * grid.CellSizeMeters);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.name = "MapGround";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(width * 0.5f, 0f, depth * 0.5f);
            ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ground.transform.localScale = new Vector3(width, depth, 1f);

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                mainTexture = BuildGridTexture()
            };
            material.mainTextureScale = new Vector2(grid.Width, grid.Depth);
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>A single-cell tile: a light fill with a thin dark border, repeated once per cell by the material's texture scale.</summary>
        private static Texture2D BuildGridTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };

            var fill = new Color(0.30f, 0.33f, 0.30f);
            var line = new Color(0.18f, 0.20f, 0.18f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onLine = x == 0 || y == 0;
                    texture.SetPixel(x, y, onLine ? line : fill);
                }
            }

            texture.Apply();
            return texture;
        }

        private static Vector3 ToUnityPosition(double simX, double simY) => new Vector3((float)simX, 0.5f, (float)simY);

        private sealed class MoverCapsuleTag : MonoBehaviour
        {
            public EntityId EntityId;
        }
    }
}
