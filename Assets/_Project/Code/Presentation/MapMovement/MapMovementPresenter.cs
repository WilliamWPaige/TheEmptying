using System;
using System.Collections.Generic;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.MapMovement
{
    /// <summary>
    /// Gray-box rendering for the map and its movers: a flat ground plane
    /// with grid lines, a capsule per mover, and click-to-inspect. Each
    /// capsule's drawn position continuously chases the simulation's actual
    /// position rather than snapping to it - the simulation can step many
    /// ticks in a single real frame (at higher game speeds, or catching up
    /// after a slow frame), so interpolating only the last of those ticks
    /// would show everything before it as an instant jump. Pure
    /// presentation: reads the module, never writes to it. A mover that's
    /// also a person (PeopleModule, set via SetPeopleModule) is colored by
    /// activity and gets a name label.
    /// </summary>
    public sealed class MapMovementPresenter : MonoBehaviour
    {
        private const float CapsuleHeight = 2f;

        private MapMovementModule _module;
        private PeopleModule _peopleModule;
        private WealthModule _wealthModule;
        private GatheringModule _gatheringModule;
        private Camera _camera;
        private InputAction _selectAction;
        private Action<EntityId> _onCapsuleSelected;
        private VisualElement _uiRoot;

        /// <summary>Floor for a mover's catch-up speed, so one that hasn't moved yet (or moved zero distance) still has a sane, non-zero speed to chase any future target with.</summary>
        private const float MinCatchUpSpeedMetersPerSecond = 1f;

        /// <summary>
        /// How much faster than the observed rate of movement the render is allowed to chase its
        /// target - headroom so the render comfortably keeps up with ordinary continuous walking
        /// (never settling into a persistent lag) while still visibly, smoothly catching up rather
        /// than snapping, even after an unusually large tick catch-up burst in one frame.
        /// </summary>
        private const float CatchUpSpeedHeadroom = 3f;

        private readonly Dictionary<EntityId, GameObject> _capsules = new Dictionary<EntityId, GameObject>();

        /// <summary>The latest known simulated position for each mover - the render chases this, it never jumps straight to it.</summary>
        private readonly Dictionary<EntityId, Vector3> _currentPositions = new Dictionary<EntityId, Vector3>();

        /// <summary>The smoothly-interpolated position actually drawn for each mover.</summary>
        private readonly Dictionary<EntityId, Vector3> _renderPositions = new Dictionary<EntityId, Vector3>();

        /// <summary>How fast each mover's render is currently allowed to close the gap to its target, in meters per real second - set from the real interval between the two most recent simulation movement steps, not a single tick.</summary>
        private readonly Dictionary<EntityId, float> _catchUpSpeeds = new Dictionary<EntityId, float>();

        private readonly List<EntityId> _staleIds = new List<EntityId>();

        private float _lastAdvanceTime = -1f;

        /// <summary>3.9h: movers within this many meters of each other (center to center) count as "stacked" and get fanned out visually - roughly within the same cell, not two people who merely happen to be nearby.</summary>
        private const float StackedClusterRadiusMeters = 0.6f;

        /// <summary>3.9h: how far apart a stacked cluster's fanned-out capsules are placed.</summary>
        private const float StackedNudgeRadiusMeters = 0.5f;

        public void Initialize(MapMovementModule module, Camera camera, InputActionMap gameplayMap, VisualElement uiRoot, Action<EntityId> onCapsuleSelected)
        {
            _module = module;
            _camera = camera;
            _selectAction = gameplayMap.FindAction("Select");
            _uiRoot = uiRoot;
            _onCapsuleSelected = onCapsuleSelected;

            BuildGround();
        }

        /// <summary>Optional: a mover that's also a person gets activity coloring and a name label. Call again after a load (with the new Simulation's module) to keep this in sync.</summary>
        public void SetPeopleModule(PeopleModule peopleModule)
        {
            _peopleModule = peopleModule;
        }

        /// <summary>Optional: adds a small class marker to a person's name label. Call again after a load to keep this in sync.</summary>
        public void SetWealthModule(WealthModule wealthModule)
        {
            _wealthModule = wealthModule;
        }

        /// <summary>Optional: 3.9f, so a person's label can show what their "Working" activity actually is (e.g. "Woodcutting"), not just the generic activity. Call again after a load to keep this in sync.</summary>
        public void SetGatheringModule(GatheringModule gatheringModule)
        {
            _gatheringModule = gatheringModule;
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
            _currentPositions.Clear();
            _renderPositions.Clear();
            _catchUpSpeeds.Clear();
            _lastAdvanceTime = -1f;
        }

        /// <summary>
        /// Called once per frame that actually stepped at least one simulation tick (never once per
        /// tick - a single frame can step many ticks at once, e.g. after a slow frame or on an
        /// unpause, and interpolating only the last of those would show everything before it as an
        /// instant jump). Records each mover's new target and, from the real time since the last call
        /// and how far that mover's target just moved, how fast the render needs to chase it to keep
        /// up without a persistent lag.
        /// </summary>
        public void OnTickAdvanced()
        {
            float now = Time.unscaledTime;
            float dt = _lastAdvanceTime >= 0f ? Mathf.Max(now - _lastAdvanceTime, 0.0001f) : Time.unscaledDeltaTime;
            _lastAdvanceTime = now;

            foreach (KeyValuePair<EntityId, MoverState> row in _module.Movers)
            {
                var worldPos = ToUnityPosition(row.Value.X, row.Value.Y);

                if (_currentPositions.TryGetValue(row.Key, out Vector3 previousTarget))
                {
                    float distance = Vector3.Distance(previousTarget, worldPos);
                    _catchUpSpeeds[row.Key] = Mathf.Max(distance / dt * CatchUpSpeedHeadroom, MinCatchUpSpeedMetersPerSecond);
                }

                _currentPositions[row.Key] = worldPos;
            }
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
                _currentPositions.Remove(id);
                _renderPositions.Remove(id);
                _catchUpSpeeds.Remove(id);
            }

            foreach (KeyValuePair<EntityId, Vector3> entry in ComputeNudgedTargets())
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
                    CreateNameLabel(capsule);
                    _capsules[entry.Key] = capsule;
                }

                Vector3 current = _renderPositions.TryGetValue(entry.Key, out Vector3 r) ? r : entry.Value;
                float speed = _catchUpSpeeds.TryGetValue(entry.Key, out float s) ? s : MinCatchUpSpeedMetersPerSecond;
                Vector3 smoothed = Vector3.MoveTowards(current, entry.Value, speed * Time.unscaledDeltaTime);
                _renderPositions[entry.Key] = smoothed;
                capsule.transform.position = smoothed;

                UpdatePersonAppearance(entry.Key, capsule);
            }
        }

        /// <summary>
        /// 3.9h: two or more movers standing at (almost) the same simulated position would otherwise
        /// render as overlapping capsules with nothing to tell them apart. Purely visual - reads
        /// _currentPositions, never touches the simulation: any such cluster is fanned out a little
        /// in a small circle around their shared spot, ordered by entity id so the fan-out is stable
        /// frame to frame instead of jittering.
        /// </summary>
        private Dictionary<EntityId, Vector3> ComputeNudgedTargets()
        {
            var clusters = new Dictionary<(int, int), List<EntityId>>();
            foreach (KeyValuePair<EntityId, Vector3> entry in _currentPositions)
            {
                var key = (Mathf.RoundToInt(entry.Value.x / StackedClusterRadiusMeters), Mathf.RoundToInt(entry.Value.z / StackedClusterRadiusMeters));
                if (!clusters.TryGetValue(key, out List<EntityId> members))
                {
                    members = new List<EntityId>();
                    clusters[key] = members;
                }

                members.Add(entry.Key);
            }

            var nudged = new Dictionary<EntityId, Vector3>(_currentPositions.Count);
            foreach (List<EntityId> members in clusters.Values)
            {
                if (members.Count == 1)
                {
                    nudged[members[0]] = _currentPositions[members[0]];
                    continue;
                }

                members.Sort((a, b) => a.Index.CompareTo(b.Index));
                for (int i = 0; i < members.Count; i++)
                {
                    float angle = i * (360f / members.Count) * Mathf.Deg2Rad;
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * StackedNudgeRadiusMeters;
                    nudged[members[i]] = _currentPositions[members[i]] + offset;
                }
            }

            return nudged;
        }

        /// <summary>Colors the capsule by activity and keeps its name label current, if (and only if) this mover is a person.</summary>
        private void UpdatePersonAppearance(EntityId id, GameObject capsule)
        {
            TextMesh label = capsule.GetComponentInChildren<TextMesh>(true);

            if (_peopleModule == null || !_peopleModule.TryGetPerson(id, out PersonState person))
            {
                if (label != null)
                {
                    label.gameObject.SetActive(false);
                }

                return;
            }

            capsule.GetComponent<Renderer>().material.color = ColorForActivity(person.Activity);

            if (label == null)
            {
                return;
            }

            label.text = $"{person.FullName}{ClassMarker(id)}\n{DescribeActivity(id, person)}";

            // No distance gating, same as a building's construction-progress label - the old
            // threshold (40) was always smaller than the strategy camera's own height-plus-setback
            // (see StrategyCameraController.PlaceAbove), so at the default camera it never passed.
            bool canShow = _camera != null;
            label.gameObject.SetActive(canShow);

            if (canShow)
            {
                BillboardUtility.FaceCameraFlat(label.transform, _camera);
            }
        }

        /// <summary>3.9f: "Working" alone doesn't say what at - when a gathering task explains it, show that instead (e.g. "Woodcutting"); otherwise fall back to the plain activity name.</summary>
        private string DescribeActivity(EntityId id, PersonState person)
        {
            if (person.Activity == PersonActivity.Working && _gatheringModule != null)
            {
                GatherTaskKind kind = _gatheringModule.GetTaskKind(id);
                if (kind != GatherTaskKind.None)
                {
                    return GatheringModule.DescribeTaskKind(kind);
                }
            }

            return PeopleModule.DescribeActivity(person.Activity);
        }

        /// <summary>A small marker appended to a person's name label: nothing for lower class, "+" for middle, "++" for upper.</summary>
        private string ClassMarker(EntityId id)
        {
            if (_wealthModule == null)
            {
                return "";
            }

            switch (_wealthModule.GetClass(id))
            {
                case TheEmptying.Core.Content.SocialClass.Upper: return " ++";
                case TheEmptying.Core.Content.SocialClass.Middle: return " +";
                default: return "";
            }
        }

        /// <summary>For tests: a mover's actual drawn (smoothed) position, as distinct from the simulation's raw position - null if it has no capsule yet.</summary>
        public Vector3? RenderedPositionForTesting(EntityId id) => _renderPositions.TryGetValue(id, out Vector3 p) ? p : (Vector3?)null;

        private static Color ColorForActivity(PersonActivity activity)
        {
            switch (activity)
            {
                case PersonActivity.Sleeping: return new Color(0.25f, 0.35f, 0.75f);
                case PersonActivity.Eating: return new Color(0.85f, 0.6f, 0.15f);
                case PersonActivity.Wandering: return new Color(0.3f, 0.75f, 0.35f);
                case PersonActivity.Commuting: return new Color(0.85f, 0.85f, 0.2f);
                case PersonActivity.Working: return new Color(0.75f, 0.35f, 0.15f);
                case PersonActivity.ForagingFood: return new Color(0.55f, 0.4f, 0.2f);
                case PersonActivity.Shopping: return new Color(0.6f, 0.45f, 0.8f);
                case PersonActivity.Socializing: return new Color(0.85f, 0.35f, 0.75f);
                case PersonActivity.Strolling: return new Color(0.35f, 0.75f, 0.75f);
                case PersonActivity.RestingAtHome: return new Color(0.5f, 0.6f, 0.9f);
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static void CreateNameLabel(GameObject capsule)
        {
            var labelObject = new GameObject("NameLabel");
            labelObject.transform.SetParent(capsule.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.4f, 0f);

            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.LowerCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 32;
            textMesh.characterSize = 0.1f;
            textMesh.color = Color.white;

            labelObject.SetActive(false);
        }

        private void HandleSelection()
        {
            if (_selectAction == null || !_selectAction.WasPressedThisFrame() || _camera == null || Mouse.current == null)
            {
                return;
            }

            SelectAt(Mouse.current.position.ReadValue());
        }

        private void SelectAt(Vector2 screenPos)
        {
            if (_camera == null || UiInputUtility.IsPointerOverUi(_uiRoot, screenPos))
            {
                return;
            }

            // A capsule just moved this same frame doesn't update the physics engine's own
            // broadphase data until the next sync point - without forcing it here, a click can
            // silently miss a mover that only just arrived at this screen position.
            Physics.SyncTransforms();

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

        /// <summary>For tests: runs the exact guarded click-to-select decision a real click at screenPosition would.</summary>
        public void ClickAtForTesting(Vector2 screenPosition) => SelectAt(screenPosition);

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
