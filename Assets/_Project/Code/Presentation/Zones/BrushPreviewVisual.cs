using UnityEngine;
using UnityEngine.Rendering;

namespace TheEmptying.Presentation.Zones
{
    public enum BrushShape
    {
        Square,
        Round
    }

    /// <summary>
    /// R18: the "Start zoning" brush preview - a flat, solid-colored
    /// square or round outline that follows the mouse, snapped to cells.
    /// Plain geometry (no texture, no transparency), same pattern as the
    /// ground-cell and selection highlights elsewhere in Presentation.
    /// </summary>
    public sealed class BrushPreviewVisual
    {
        private const int CircleSegments = 24;
        private static Mesh s_quadMesh;
        private static Mesh s_circleMesh;

        private readonly GameObject _instance;
        private readonly MeshFilter _meshFilter;
        private readonly Material _material;
        private BrushShape? _currentShape;

        public GameObject GameObject => _instance;

        public BrushPreviewVisual(Transform parent)
        {
            _instance = new GameObject("Brush Preview");
            _instance.transform.SetParent(parent, worldPositionStays: true);
            _instance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _meshFilter = _instance.AddComponent<MeshFilter>();
            var renderer = _instance.AddComponent<MeshRenderer>();
            _material = BuildMaterial();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            SetShape(BrushShape.Square);
            SetActive(false);
        }

        public void SetActive(bool active) => _instance.SetActive(active);

        public void Destroy() => Object.Destroy(_instance);

        public void SetColor(Color color) => _material.color = color;

        public void SetShape(BrushShape shape)
        {
            if (_currentShape == shape)
            {
                return;
            }

            _currentShape = shape;
            _meshFilter.sharedMesh = shape == BrushShape.Round ? GetCircleMesh() : GetQuadMesh();
        }

        /// <summary>Positions and sizes the preview; worldSize is the full side length (square) or diameter (round), in meters.</summary>
        public void UpdateGeometry(Vector3 renderCenter, float worldSize)
        {
            _instance.transform.position = renderCenter;
            _instance.transform.localScale = new Vector3(worldSize, worldSize, 1f);
        }

        /// <summary>Double-sided, so a hand-rolled mesh's winding direction (the circle; the quad copies a real primitive) never has to be exactly right for the preview to actually be visible from above.</summary>
        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "BrushPreviewMaterial" };
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
            }

            return material;
        }

        /// <summary>A flat, 1x1 quad centered at the origin, copied from Unity's own PrimitiveType.Quad so its winding is known-correct, then thrown away.</summary>
        private static Mesh GetQuadMesh()
        {
            if (s_quadMesh != null)
            {
                return s_quadMesh;
            }

            GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Quad);
            s_quadMesh = temporary.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(temporary);

            return s_quadMesh;
        }

        /// <summary>A flat disc of diameter 1 centered at the origin, so UpdateGeometry's scale is the diameter directly.</summary>
        private static Mesh GetCircleMesh()
        {
            if (s_circleMesh != null)
            {
                return s_circleMesh;
            }

            var vertices = new Vector3[CircleSegments + 1];
            var triangles = new int[CircleSegments * 3];

            vertices[0] = Vector3.zero;
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * 2f * Mathf.PI / CircleSegments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.5f;

                int triangleBase = i * 3;
                triangles[triangleBase] = 0;
                triangles[triangleBase + 1] = i + 1;
                triangles[triangleBase + 2] = i + 2 > CircleSegments ? 1 : i + 2;
            }

            var mesh = new Mesh { name = "BrushPreviewCircleMesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            s_circleMesh = mesh;
            return mesh;
        }
    }
}
