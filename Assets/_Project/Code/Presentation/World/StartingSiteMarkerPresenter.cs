using UnityEngine;
using UnityEngine.Rendering;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// R11: the clickable marker at the (for now, one) starting site - a
    /// tall thin pole with a small flag, built from generic primitive
    /// shapes, visible through the floating origin. Destroyed for good
    /// once the kingdom is founded.
    /// </summary>
    public sealed class StartingSiteMarkerPresenter : MonoBehaviour
    {
        private const float PoleHeight = 4f;
        private const float PoleRadius = 0.08f;

        private FloatingOrigin _floatingOrigin;
        private Vector3 _worldPosition;

        public void Initialize(FloatingOrigin floatingOrigin, double worldX, double worldZ)
        {
            _floatingOrigin = floatingOrigin;
            _worldPosition = new Vector3((float)worldX, 0f, (float)worldZ);

            gameObject.name = "Starting Site Marker";
            transform.position = _floatingOrigin.WorldToRender(_worldPosition);

            BuildShapes();

            _floatingOrigin.Shifted += OnFloatingOriginShifted;
        }

        private void OnDestroy()
        {
            if (_floatingOrigin != null)
            {
                _floatingOrigin.Shifted -= OnFloatingOriginShifted;
            }
        }

        private void OnFloatingOriginShifted(Vector3 delta)
        {
            transform.position -= delta;
        }

        /// <summary>Whether a clicked GameObject is part of this marker (its pole or its flag).</summary>
        public bool IsPart(GameObject hit) => hit != null && hit.transform.parent == transform;

        private void BuildShapes()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(transform, false);
            pole.transform.localScale = new Vector3(PoleRadius, PoleHeight * 0.5f, PoleRadius);
            // R20a: the pole's bottom, not its pivot (its own center), rests on the ground.
            pole.transform.localPosition = new Vector3(0f, GroundPlacement.PivotHeightAboveBase(pole), 0f);
            var poleRenderer = pole.GetComponent<MeshRenderer>();
            poleRenderer.sharedMaterial = new Material(shader) { name = "SiteMarkerPoleMaterial", color = new Color(0.5f, 0.38f, 0.25f) };
            poleRenderer.shadowCastingMode = ShadowCastingMode.Off;

            GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "Flag";
            flag.transform.SetParent(transform, false);
            flag.transform.localPosition = new Vector3(0.45f, PoleHeight - 0.5f, 0f);
            flag.transform.localScale = new Vector3(0.9f, 0.55f, 0.04f);
            var flagRenderer = flag.GetComponent<MeshRenderer>();
            flagRenderer.sharedMaterial = new Material(shader) { name = "SiteMarkerFlagMaterial", color = new Color(0.85f, 0.2f, 0.2f) };
            flagRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
