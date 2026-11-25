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

        /// <summary>R18: the zoning view's smooth switch in/out, and which way it faces.</summary>
        public const string ZoningTransitionSecondsSettingId = "setting.zoning.camera_transition_seconds";
        public const string ZoningKeepRotationFacingSettingId = "setting.zoning.keep_rotation_facing";

        /// <summary>Placeholder: a fixed downward look angle. Not a setting - the spec only calls out pan speed, zoom limits/speed, rotate speed and edge size as tunables.</summary>
        private const float PitchAngleDegrees = 50f;

        /// <summary>R18: the zoning view looks straight down.</summary>
        private const float ZoningPitchAngleDegrees = 90f;

        private enum ViewMode
        {
            Normal,
            EnteringZoning,
            Zoning,
            ExitingZoning
        }

        private InputAction _panAction;
        private InputAction _rotateAction;
        private InputAction _zoomAction;
        private InputAction _rotateDragAction;

        private SettingsRegistry _settings;
        private VisualElement _uiRoot;
        private MapModule _mapModule;
        private FloatingOrigin _floatingOrigin;
        private Camera _camera;

        private Vector3 _focusPoint;
        private float _height;
        private float _yaw;
        private float _pitchDegrees = PitchAngleDegrees;

        private ViewMode _viewMode = ViewMode.Normal;
        private bool _zoningKeepRotation;
        private float _transitionElapsed;
        private float _transitionDuration;
        private float _transitionStartPitch;
        private float _transitionEndPitch;
        private float _transitionStartYaw;
        private float _transitionEndYaw;
        private float _transitionStartOrthoSize;
        private float _transitionEndOrthoSize;
        private float _transitionStartHeight;
        private float _transitionEndHeight;
        private Vector3 _transitionStartFocusPoint;
        private Vector3 _transitionEndFocusPoint;

        /// <summary>Saved the moment EnterZoningView is called; ExitZoningView restores exactly this (position, height, rotation), discarding any panning/zooming done while zoning.</summary>
        private Vector3 _preZoningFocusPoint;
        private float _preZoningHeight;
        private float _preZoningYaw;

        /// <summary>Set by the Escape menu while it (or its Options/confirm sub-panels) is open: the camera stays completely still, same as while a text field is focused.</summary>
        public bool InputSuspended { get; set; }

        /// <summary>The ground point the camera is centered on, as a true world position (always at y = 0; panning never changes height). Used by the Spawn cheat to place a model "at the camera's center", and by the Map cheat's "go to"/corner buttons to read where it is.</summary>
        public Vector3 FocusPoint => _focusPoint;

        /// <summary>The camera's current zoom height, in meters. Used by the world map view to decide when to switch to the low-detail view.</summary>
        public float Height => _height;

        /// <summary>R18: true from the moment EnterZoningView is called until ExitZoningView's own transition finishes.</summary>
        public bool IsInZoningView => _viewMode != ViewMode.Normal;

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
            _camera = GetComponent<Camera>();

            // New game: the camera starts at the world center (R6).
            _focusPoint = new Vector3((float)(mapModule.WorldWidthMeters * 0.5), 0f, (float)(mapModule.WorldDepthMeters * 0.5));
            _yaw = 0f;
            _height = (float)((settings.Get(ZoomMinHeightSettingId) + settings.Get(ZoomMaxHeightSettingId)) * 0.5);

            ApplyTransform();
        }

        /// <summary>
        /// R18: begins the smooth switch into the orthographic top-down
        /// zoning view - snapshots the current position/height/rotation
        /// (ExitZoningView restores exactly this) and starts animating
        /// pitch (and, unless the facing setting says to keep the current
        /// rotation, yaw back to north) toward it. A no-op if already
        /// zoning or mid-transition.
        /// </summary>
        public void EnterZoningView()
        {
            // Zoning (already there) and EnteringZoning (already heading there) are no-ops; Normal and
            // ExitingZoning (reverses an exit that hasn't finished yet) both start a fresh entering transition.
            if (_viewMode == ViewMode.Zoning || _viewMode == ViewMode.EnteringZoning || _camera == null)
            {
                return;
            }

            if (_viewMode == ViewMode.Normal)
            {
                // Only taken on the true starting point - never overwritten by an interrupted exit's transient state.
                _preZoningFocusPoint = _focusPoint;
                _preZoningHeight = _height;
                _preZoningYaw = _yaw;

                _camera.orthographic = true;
                _camera.orthographicSize = ComputeMatchingOrthoSize(_pitchDegrees, _height);
            }

            _zoningKeepRotation = _settings.Get(ZoningKeepRotationFacingSettingId) != 0;

            _transitionStartPitch = _pitchDegrees;
            _transitionEndPitch = ZoningPitchAngleDegrees;
            _transitionStartYaw = _yaw;
            _transitionEndYaw = _zoningKeepRotation ? _yaw : 0f;
            _transitionStartOrthoSize = _camera.orthographicSize;
            _transitionEndOrthoSize = _height;
            _transitionStartHeight = _height;
            _transitionEndHeight = _height;
            _transitionStartFocusPoint = _focusPoint;
            _transitionEndFocusPoint = _focusPoint;

            BeginTransition(ViewMode.EnteringZoning);
        }

        /// <summary>R18: begins the smooth switch back out, toward exactly the position/height/rotation EnterZoningView snapshotted - discarding any panning or zooming done while zoning. A no-op unless currently in the steady zoning state.</summary>
        public void ExitZoningView()
        {
            // Normal (already there) and ExitingZoning (already heading there) are no-ops; Zoning and
            // EnteringZoning (reverses an entry that hasn't finished yet) both start a fresh exiting transition.
            if (_viewMode == ViewMode.Normal || _viewMode == ViewMode.ExitingZoning || _camera == null)
            {
                return;
            }

            _transitionStartPitch = _pitchDegrees;
            _transitionEndPitch = PitchAngleDegrees;
            _transitionStartYaw = _yaw;
            _transitionEndYaw = _preZoningYaw;
            _transitionStartOrthoSize = _camera.orthographicSize;
            _transitionEndOrthoSize = ComputeMatchingOrthoSize(PitchAngleDegrees, _preZoningHeight);
            _transitionStartHeight = _height;
            _transitionEndHeight = _preZoningHeight;
            _transitionStartFocusPoint = _focusPoint;
            _transitionEndFocusPoint = _preZoningFocusPoint;

            BeginTransition(ViewMode.ExitingZoning);
        }

        private void BeginTransition(ViewMode mode)
        {
            _transitionDuration = Mathf.Max(0.01f, (float)_settings.Get(ZoningTransitionSecondsSettingId));
            _transitionElapsed = 0f;
            _viewMode = mode;
        }

        /// <summary>The orthographic size that shows roughly the same ground-plane extent a perspective camera at this pitch/height currently shows, so flipping Camera.orthographic on or off never visibly "pops".</summary>
        private float ComputeMatchingOrthoSize(float pitchDegrees, float height)
        {
            float pitchRadians = pitchDegrees * Mathf.Deg2Rad;
            float distance = height / Mathf.Max(0.01f, Mathf.Sin(pitchRadians));
            float halfFovRadians = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            return distance * Mathf.Tan(halfFovRadians);
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
            if (_settings == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            if (_viewMode == ViewMode.EnteringZoning || _viewMode == ViewMode.ExitingZoning)
            {
                AdvanceTransition(dt);
                return;
            }

            if (InputSuspended || UiInputUtility.IsTextFieldFocused(_uiRoot))
            {
                return;
            }

            float panSpeed = (float)_settings.Get(PanSpeedSettingId);
            float zoomSpeed = (float)_settings.Get(ZoomSpeedSettingId);
            float zoomMin = (float)_settings.Get(ZoomMinHeightSettingId);
            float zoomMax = (float)_settings.Get(ZoomMaxHeightSettingId);
            float rotateSpeed = (float)_settings.Get(RotateSpeedSettingId);
            float dragSensitivity = (float)_settings.Get(RotateDragSensitivitySettingId);
            bool edgeScrollingEnabled = _settings.Get(EdgeScrollingSettingId) != 0;
            float edgeSize = (float)_settings.Get(EdgeScrollSizeSettingId);

            // R9: the wheel over a UI panel scrolls that panel, never the camera.
            bool pointerOverUi = Mouse.current != null && UiInputUtility.IsPointerOverUi(_uiRoot, Mouse.current.position.ReadValue());
            float scroll = pointerOverUi ? 0f : _zoomAction?.ReadValue<float>() ?? 0f;
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

            // R18: North-up zoning is fixed - Q/E and the rotate-drag mouse button do nothing while it's active.
            bool rotationLocked = _viewMode == ViewMode.Zoning && !_zoningKeepRotation;
            if (!rotationLocked)
            {
                float rotateInput = _rotateAction?.ReadValue<float>() ?? 0f;
                _yaw += rotateInput * rotateSpeed * dt;

                if (_rotateDragAction != null && _rotateDragAction.IsPressed() && Mouse.current != null)
                {
                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                    _yaw += mouseDelta.x * dragSensitivity;
                }
            }

            (double clampedX, double clampedZ) = _mapModule.ClampToWorld(_focusPoint.x, _focusPoint.z);
            _focusPoint.x = (float)clampedX;
            _focusPoint.z = (float)clampedZ;

            // R6: keep the camera near the rendering origin so nothing jitters anywhere on a 15 km map. _focusPoint itself is never shifted - only the Transform ApplyTransform computes below is.
            _floatingOrigin.CheckAndShift(_focusPoint);

            // R18: the zoning view maps the same zoom range straight onto the orthographic size, so zooming feels the same.
            if (_viewMode == ViewMode.Zoning)
            {
                _camera.orthographicSize = _height;
            }

            ApplyTransform();
        }

        /// <summary>R18: drives EnterZoningView/ExitZoningView's smooth switch - pitch, facing, orthographic size, and (exiting only) height/focus point all ease toward their transition's end values, landing on them exactly at t=1.</summary>
        private void AdvanceTransition(float dt)
        {
            _transitionElapsed += dt;
            float t = Mathf.Clamp01(_transitionElapsed / _transitionDuration);

            _pitchDegrees = Mathf.Lerp(_transitionStartPitch, _transitionEndPitch, t);
            _yaw = Mathf.LerpAngle(_transitionStartYaw, _transitionEndYaw, t);
            _height = Mathf.Lerp(_transitionStartHeight, _transitionEndHeight, t);
            _focusPoint = Vector3.Lerp(_transitionStartFocusPoint, _transitionEndFocusPoint, t);
            _camera.orthographicSize = Mathf.Lerp(_transitionStartOrthoSize, _transitionEndOrthoSize, t);

            _floatingOrigin.CheckAndShift(_focusPoint);
            ApplyTransform();

            if (t < 1f)
            {
                return;
            }

            if (_viewMode == ViewMode.EnteringZoning)
            {
                _viewMode = ViewMode.Zoning;
                return;
            }

            // ExitingZoning: land exactly on the pre-zoning snapshot, discarding any float error from the lerps above.
            _viewMode = ViewMode.Normal;
            _camera.orthographic = false;
            _pitchDegrees = PitchAngleDegrees;
            _yaw = _preZoningYaw;
            _height = _preZoningHeight;
            _focusPoint = _preZoningFocusPoint;
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
            // R18a: height / tan(pitch) has a pole exactly at pitch = 90 (straight down, the zoning view's
            // own pitch) - float rounding can push 90 degrees just past true pi/2, making Mathf.Tan return a
            // huge NEGATIVE number there, which a naive Mathf.Max(epsilon, tan) guard clamps the wrong way
            // (up to +epsilon), flipping the division into a wildly wrong, enormous horizontalDistance. Using
            // sin/cos instead has no pole at 90 (cos is simply ~0 there, giving ~0 distance, correctly) - sin
            // only approaches 0 near pitch 0/180, nowhere this camera's pitch (50 to 90) ever goes.
            float pitchRadians = _pitchDegrees * Mathf.Deg2Rad;
            float horizontalDistance = _height * Mathf.Cos(pitchRadians) / Mathf.Max(0.0001f, Mathf.Sin(pitchRadians));

            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 back = yawRotation * Vector3.back;
            Vector3 worldPosition = _focusPoint + back * horizontalDistance + Vector3.up * _height;
            Vector3 renderPosition = _floatingOrigin.WorldToRender(worldPosition);

            transform.SetPositionAndRotation(renderPosition, Quaternion.Euler(_pitchDegrees, _yaw, 0f));
        }
    }
}
