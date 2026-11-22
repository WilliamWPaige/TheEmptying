using TheEmptying.Core.Content;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheEmptying.Presentation.MapMovement
{
    /// <summary>
    /// A gray-box strategy camera: pan with WASD (and, if the edge-scrolling
    /// setting is on, screen edges), zoom with the wheel (dollying along its
    /// own forward direction), rotate around its ground focus point with Q
    /// and E. No unit selection or click-to-move.
    /// </summary>
    public sealed class StrategyCameraController : MonoBehaviour
    {
        public const string EdgeScrollingSettingId = "setting.camera.edge_scrolling";

        /// <summary>3.10b4: WASD panning was reported broken in the real Editor with no reproducible
        /// cause found by reading the code (see Docs/KnownIssues.md). Logs on every press/release edge
        /// of any of W/A/S/D: the raw key states, the Pan action's read value, and whether the action
        /// and its map are enabled - so the next real repro shows directly where the value goes missing.</summary>
        public const string LogCameraInputSettingId = "setting.debug.log_camera_input";

        private const float PanMetersPerSecond = 30f;
        private const float EdgePanBorderPixels = 14f;
        private const float ZoomMetersPerSecond = 40f;
        private const float RotateDegreesPerSecond = 90f;
        private const float MinHeight = 8f;
        private const float MaxHeight = 200f;

        private InputAction _panAction;
        private InputAction _rotateAction;
        private InputAction _zoomAction;
        private SettingsRegistry _settings;
        private bool _lastAnyPanKeyDown;

        public void Initialize(InputActionMap gameplayMap, SettingsRegistry settings)
        {
            _panAction = gameplayMap.FindAction("Pan");
            _rotateAction = gameplayMap.FindAction("Rotate");
            _zoomAction = gameplayMap.FindAction("Zoom");
            _settings = settings;
        }

        public void PlaceAbove(Vector3 lookAtGroundPoint, float height)
        {
            transform.position = lookAtGroundPoint + new Vector3(0f, Mathf.Clamp(height, MinHeight, MaxHeight), -height * 0.6f);
            transform.LookAt(lookAtGroundPoint);
        }

        private void Update()
        {
            LogCameraInputIfEnabled();

            if (_panAction == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            Vector2 pan = _panAction.ReadValue<Vector2>();
            if (_settings != null && _settings.Get(EdgeScrollingSettingId) != 0)
            {
                pan += EdgePan();
            }

            pan = Vector2.ClampMagnitude(pan, 1f);

            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();

            transform.position += (forward * pan.y + right * pan.x) * (PanMetersPerSecond * dt);

            float rotate = _rotateAction.ReadValue<float>();
            if (Mathf.Abs(rotate) > 0.0001f)
            {
                transform.RotateAround(GroundFocusPoint(), Vector3.up, rotate * RotateDegreesPerSecond * dt);
            }

            float zoom = _zoomAction.ReadValue<float>();
            if (Mathf.Abs(zoom) > 0.0001f)
            {
                transform.position += transform.forward * (Mathf.Sign(zoom) * ZoomMetersPerSecond * dt);

                Vector3 position = transform.position;
                position.y = Mathf.Clamp(position.y, MinHeight, MaxHeight);
                transform.position = position;
            }
        }

        private void LogCameraInputIfEnabled()
        {
            if (_settings == null || _settings.Get(LogCameraInputSettingId) == 0 || Keyboard.current == null)
            {
                return;
            }

            bool anyPanKeyDown = Keyboard.current.wKey.isPressed || Keyboard.current.aKey.isPressed
                || Keyboard.current.sKey.isPressed || Keyboard.current.dKey.isPressed;

            if (anyPanKeyDown == _lastAnyPanKeyDown)
            {
                return;
            }

            _lastAnyPanKeyDown = anyPanKeyDown;
            Vector2 panValue = _panAction?.ReadValue<Vector2>() ?? Vector2.zero;
            Debug.Log($"[CameraInput] anyWasdDown={anyPanKeyDown} w={Keyboard.current.wKey.isPressed} a={Keyboard.current.aKey.isPressed} s={Keyboard.current.sKey.isPressed} d={Keyboard.current.dKey.isPressed} panReadValue={panValue} panActionNull={_panAction == null} panActionEnabled={_panAction?.enabled} mapEnabled={_panAction?.actionMap?.enabled}");
        }

        private Vector2 EdgePan()
        {
            if (Mouse.current == null)
            {
                return Vector2.zero;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            var edge = Vector2.zero;

            if (mousePosition.x <= EdgePanBorderPixels)
            {
                edge.x -= 1f;
            }
            else if (mousePosition.x >= Screen.width - EdgePanBorderPixels)
            {
                edge.x += 1f;
            }

            if (mousePosition.y <= EdgePanBorderPixels)
            {
                edge.y -= 1f;
            }
            else if (mousePosition.y >= Screen.height - EdgePanBorderPixels)
            {
                edge.y += 1f;
            }

            return edge;
        }

        private Vector3 GroundFocusPoint()
        {
            Vector3 origin = transform.position;
            Vector3 direction = transform.forward;

            if (Mathf.Abs(direction.y) > 0.0001f)
            {
                float t = -origin.y / direction.y;
                if (t > 0f)
                {
                    return origin + direction * t;
                }
            }

            return origin;
        }
    }
}
