using TheEmptying.Core.Content;
using TheEmptying.Modules.Map;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// Strategy-style camera: orbits a focus point on the ground at a fixed
    /// downward pitch, panning, zooming and rotating around it. Runs on its
    /// own Update() against real (unscaled) time, so it keeps working while
    /// the game is paused or at any game speed. Reads its tunable numbers
    /// from settings every frame, since they can change mid-game.
    ///
    /// The focus point it tracks is always a true, unshifted world
    /// position (R6) - the world can be clamped to by it, and other
    /// systems (Spawn, Map cheats) can read or set it in world terms. Only
    /// the rendered Transform itself goes through the FloatingOrigin.
    /// </summary>
    public sealed class StrategyCameraController : MonoBehaviour
    {
        public const string PanSpeedSettingId = "setting.camera.pan_speed";
        public const string ZoomSpeedSettingId = "setting.camera.zoom_speed";
        public const string ZoomMinHeightSettingId = "setting.camera.zoom_min_height";
        public const string ZoomMaxHeightSettingId = "setting.camera.zoom_max_height";
        public const string RotateSpeedSettingId = "setting.camera.rotate_speed";
        public const string RotateDragSensitivitySettingId = "setting.camera.rotate_drag_sensitivity";
        public const string EdgeScrollingSettingId = "setting.camera.edge_scrolling";
        public const string EdgeScrollSizeSettingId = "setting.camera.edge_scroll_size";

        /// <summary>Placeholder: a fixed downward look angle. Not a setting - the spec only calls out pan speed, zoom limits/speed, rotate speed and edge size as tunables.</summary>
        private const float PitchAngleDegrees = 50f;

        private InputAction _panAction;
        private InputAction _rotateAction;
        private InputAction _zoomAction;
        private InputAction _rotateDragAction;

        private SettingsRegistry _settings;
        private VisualElement _uiRoot;
        private MapModule _mapModule;
        private FloatingOrigin _floatingOrigin;

        private Vector3 _focusPoint;
        private float _height;
        private float _yaw;

        /// <summary>Set by the Escape menu while it (or its Options/confirm sub-panels) is open: the camera stays completely still, same as while a text field is focused.</summary>
        public bool InputSuspended { get; set; }

        /// <summary>The ground point the camera is centered on, as a true world position (always at y = 0; panning never changes height). Used by the Spawn cheat to place a model "at the camera's center", and by the Map cheat's "go to"/corner buttons to read where it is.</summary>
        public Vector3 FocusPoint => _focusPoint;

        /// <summary>The camera's current zoom height, in meters. Used by the world map view to decide when to switch to the low-detail view.</summary>
        public float Height => _height;

        public void Initialize(InputActionMap gameplayMap, SettingsRegistry settings, VisualElement uiRoot, MapModule mapModule, FloatingOrigin floatingOrigin)
        {
            _panAction = gameplayMap.FindAction("Pan");
            _rotateAction = gameplayMap.FindAction("Rotate");
            _zoomAction = gameplayMap.FindAction("Zoom");
            _rotateDragAction = gameplayMap.FindAction("RotateDrag");

            _settings = settings;
            _uiRoot = uiRoot;
            _mapModule = mapModule;
            _floatingOrigin = floatingOrigin;

            // New game: the camera starts at the world center (R6).
            _focusPoint = new Vector3((float)(mapModule.WorldWidthMeters * 0.5), 0f, (float)(mapModule.WorldDepthMeters * 0.5));
            _yaw = 0f;
            _height = (float)((settings.Get(ZoomMinHeightSettingId) + settings.Get(ZoomMaxHeightSettingId)) * 0.5);

            ApplyTransform();
        }

        /// <summary>After a load, the simulation (and its SettingsRegistry/MapModule) is a new instance; point the camera at it.</summary>
        public void Rebind(SettingsRegistry settings, MapModule mapModule)
        {
            _settings = settings;
            _mapModule = mapModule;
        }

        /// <summary>Moves the camera to a world position directly (the Map cheat's "go to" fields and corner/center buttons), clamped to the world's edges like any other pan.</summary>
        public void SetFocusPoint(float worldX, float worldZ)
        {
            (double clampedX, double clampedZ) = _mapModule.ClampToWorld(worldX, worldZ);
            _focusPoint.x = (float)clampedX;
            _focusPoint.z = (float)clampedZ;

            _floatingOrigin.CheckAndShift(_focusPoint);
            ApplyTransform();
        }

        private void Update()
        {
            if (_settings == null || InputSuspended || UiInputUtility.IsTextFieldFocused(_uiRoot))
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            float panSpeed = (float)_settings.Get(PanSpeedSettingId);
            float zoomSpeed = (float)_settings.Get(ZoomSpeedSettingId);
            float zoomMin = (float)_settings.Get(ZoomMinHeightSettingId);
            float zoomMax = (float)_settings.Get(ZoomMaxHeightSettingId);
            float rotateSpeed = (float)_settings.Get(RotateSpeedSettingId);
            float dragSensitivity = (float)_settings.Get(RotateDragSensitivitySettingId);
            bool edgeScrollingEnabled = _settings.Get(EdgeScrollingSettingId) != 0;
            float edgeSize = (float)_settings.Get(EdgeScrollSizeSettingId);

            float scroll = _zoomAction?.ReadValue<float>() ?? 0f;
            if (scroll != 0f)
            {
                _height = Mathf.Clamp(_height - scroll * zoomSpeed, zoomMin, zoomMax);
            }

            // Pan speed scales with zoom: faster while zoomed out.
            float effectivePanSpeed = panSpeed * (_height / Mathf.Max(zoomMin, 0.01f));

            Vector2 panInput = _panAction?.ReadValue<Vector2>() ?? Vector2.zero;
            if (edgeScrollingEnabled)
            {
                panInput += ReadEdgeScrollInput(edgeSize);
            }

            panInput = Vector2.ClampMagnitude(panInput, 1f);

            if (panInput != Vector2.zero)
            {
                Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
                Vector3 forward = yawRotation * Vector3.forward;
                Vector3 right = yawRotation * Vector3.right;
                _focusPoint += (forward * panInput.y + right * panInput.x) * effectivePanSpeed * dt;
            }

            float rotateInput = _rotateAction?.ReadValue<float>() ?? 0f;
            _yaw += rotateInput * rotateSpeed * dt;

            if (_rotateDragAction != null && _rotateDragAction.IsPressed() && Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                _yaw += mouseDelta.x * dragSensitivity;
            }

            (double clampedX, double clampedZ) = _mapModule.ClampToWorld(_focusPoint.x, _focusPoint.z);
            _focusPoint.x = (float)clampedX;
            _focusPoint.z = (float)clampedZ;

            // R6: keep the camera near the rendering origin so nothing jitters anywhere on a 15 km map. _focusPoint itself is never shifted - only the Transform ApplyTransform computes below is.
            _floatingOrigin.CheckAndShift(_focusPoint);

            ApplyTransform();
        }

        private static Vector2 ReadEdgeScrollInput(float edgeSize)
        {
            if (Mouse.current == null)
            {
                return Vector2.zero;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            var input = Vector2.zero;

            if (mousePosition.x <= edgeSize)
            {
                input.x -= 1f;
            }
            else if (mousePosition.x >= Screen.width - edgeSize)
            {
                input.x += 1f;
            }

            if (mousePosition.y <= edgeSize)
            {
                input.y -= 1f;
            }
            else if (mousePosition.y >= Screen.height - edgeSize)
            {
                input.y += 1f;
            }

            return input;
        }

        private void ApplyTransform()
        {
            float pitchRadians = PitchAngleDegrees * Mathf.Deg2Rad;
            float horizontalDistance = _height / Mathf.Tan(pitchRadians);

            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 back = yawRotation * Vector3.back;
            Vector3 worldPosition = _focusPoint + back * horizontalDistance + Vector3.up * _height;
            Vector3 renderPosition = _floatingOrigin.WorldToRender(worldPosition);

            transform.SetPositionAndRotation(renderPosition, Quaternion.Euler(PitchAngleDegrees, _yaw, 0f));
        }
    }
}
