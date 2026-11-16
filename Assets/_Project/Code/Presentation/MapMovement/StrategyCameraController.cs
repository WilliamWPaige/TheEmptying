using UnityEngine;
using UnityEngine.InputSystem;

namespace TheEmptying.Presentation.MapMovement
{
    /// <summary>
    /// A gray-box strategy camera: pan with WASD and screen edges, zoom with
    /// the wheel (dollying along its own forward direction), rotate around
    /// its ground focus point with Q and E. No unit selection or click-to-move.
    /// </summary>
    public sealed class StrategyCameraController : MonoBehaviour
    {
        private const float PanMetersPerSecond = 30f;
        private const float EdgePanBorderPixels = 14f;
        private const float ZoomMetersPerSecond = 40f;
        private const float RotateDegreesPerSecond = 90f;
        private const float MinHeight = 8f;
        private const float MaxHeight = 200f;

        private InputAction _panAction;
        private InputAction _rotateAction;
        private InputAction _zoomAction;

        public void Initialize(InputActionMap gameplayMap)
        {
            _panAction = gameplayMap.FindAction("Pan");
            _rotateAction = gameplayMap.FindAction("Rotate");
            _zoomAction = gameplayMap.FindAction("Zoom");
        }

        public void PlaceAbove(Vector3 lookAtGroundPoint, float height)
        {
            transform.position = lookAtGroundPoint + new Vector3(0f, Mathf.Clamp(height, MinHeight, MaxHeight), -height * 0.6f);
            transform.LookAt(lookAtGroundPoint);
        }

        private void Update()
        {
            if (_panAction == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            Vector2 pan = _panAction.ReadValue<Vector2>();
            pan += EdgePan();
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
