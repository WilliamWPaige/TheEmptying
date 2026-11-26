using System.Collections.Generic;
using TheEmptying.Modules.Map;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18b: a zone's painted cells drawn as a merged, transparent-
    /// tinted mesh (one quad per cell) flat on the ground, through the
    /// floating origin - shared by the live paint-in-progress preview
    /// and every placed zone. A zone's cells may be several separate
    /// pieces; one mesh covers all of them at once. The GameObject sits
    /// at its parent's local origin (zero local offset) - the parent
    /// (ZonePresenter's or ZoningMenuPresenter's shared root) is what the
    /// floating origin shifts (by subtracting each shift's delta from its
    /// own position), so the vertices here are baked in RAW WORLD
    /// coordinates, not render space - the parent's own shifted position
    /// supplies the render-space offset. (R18d: this used to also run the
    /// cell positions through FloatingOrigin.WorldToRender before baking
    /// them, which double-applied the offset together with the parent's
    /// own compensation and sent the mesh far off screen - fixed by
    /// leaving the raw world coordinates for the parent to offset.)
    /// </summary>
    public sealed class ZoneVisual
    {
        private readonly GameObject _instance;
        private readonly MeshFilter _meshFilter;
        private readonly MeshCollider _meshCollider;
        private readonly Material _material;
        private readonly Mesh _mesh;

        public GameObject GameObject => _instance;

        public ZoneVisual(Transform parent, Color color, bool collidable)
        {
            _instance = new GameObject("Zone");
            _instance.transform.SetParent(parent, worldPositionStays: true);
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;

            _mesh = new Mesh { name = "ZoneCellsMesh" };
            _meshFilter = _instance.AddComponent<MeshFilter>();
            _meshFilter.sharedMesh = _mesh;

            var renderer = _instance.AddComponent<MeshRenderer>();
            _material = BuildTransparentMaterial();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (collidable)
            {
                _meshCollider = _instance.AddComponent<MeshCollider>();
            }

            SetColor(color);
        }

        public void SetActive(bool active) => _instance.SetActive(active);

        public void Destroy() => Object.Destroy(_instance);

        /// <summary>
        /// The fill color's alpha is the transparent tint's own strength;
        /// callers pass a color already at the tint alpha they want.
        /// R18d: Material.color sets the shader property named "_Color",
        /// which the URP Lit/Unlit shaders this material is built from
        /// don't have (they use "_BaseColor" instead) - set directly on
        /// whichever property the shader actually has.
        /// </summary>
        public void SetColor(Color color)
        {
            if (_material.HasProperty("_BaseColor"))
            {
                _material.SetColor("_BaseColor", color);
            }
            else
            {
                _material.color = color;
            }
        }

        /// <summary>Rebuilds the mesh (and, if collidable, the collider) from this exact cell set - one quad per cell, merged into a single mesh so several separate pieces still draw and select as one zone.</summary>
        public void UpdateGeometry(IEnumerable<CellCoord> cells, double cellSizeMeters)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float size = (float)cellSizeMeters;

            foreach (CellCoord cell in cells)
            {
                var worldMin = new Vector3((float)(cell.X * cellSizeMeters), 0.02f, (float)(cell.Z * cellSizeMeters));

                int baseIndex = vertices.Count;
                vertices.Add(worldMin);
                vertices.Add(worldMin + new Vector3(0f, 0f, size));
                vertices.Add(worldMin + new Vector3(size, 0f, size));
                vertices.Add(worldMin + new Vector3(size, 0f, 0f));

                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            if (_meshCollider != null)
            {
                // A MeshCollider only rebuilds its collision data when sharedMesh actually changes reference,
                // so this re-assigns even though it's already set, to pick up every rebuilt geometry.
                _meshCollider.sharedMesh = null;
                _meshCollider.sharedMesh = _mesh;
            }
        }

        /// <summary>Transparent so the ground, ponds and anything else underneath a zone's tint still show through - the standard URP script recipe for toggling a Lit/Unlit material's Surface Type to Transparent.</summary>
        private static Material BuildTransparentMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            var material = new Material(shader) { name = "ZoneMaterial" };

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            return material;
        }
    }
}
