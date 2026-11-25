using System.Collections.Generic;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation.World;
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
    /// at its parent's local origin - the parent (ZonePresenter's or
    /// ZoningMenuPresenter's shared root) is what the floating origin
    /// shifts, so every vertex here is baked in render space once, at
    /// UpdateGeometry time, and stays correct until the next shift moves
    /// the whole parent.
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

        /// <summary>The fill color's alpha is the transparent tint's own strength; callers pass a color already at the tint alpha they want.</summary>
        public void SetColor(Color color) => _material.color = color;

        /// <summary>Rebuilds the mesh (and, if collidable, the collider) from this exact cell set - one quad per cell, merged into a single mesh so several separate pieces still draw and select as one zone.</summary>
        public void UpdateGeometry(IEnumerable<CellCoord> cells, double cellSizeMeters, FloatingOrigin floatingOrigin)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float size = (float)cellSizeMeters;

            foreach (CellCoord cell in cells)
            {
                var worldMin = new Vector3((float)(cell.X * cellSizeMeters), 0.02f, (float)(cell.Z * cellSizeMeters));
                Vector3 renderMin = floatingOrigin.WorldToRender(worldMin);

                int baseIndex = vertices.Count;
                vertices.Add(renderMin);
                vertices.Add(renderMin + new Vector3(0f, 0f, size));
                vertices.Add(renderMin + new Vector3(size, 0f, size));
                vertices.Add(renderMin + new Vector3(size, 0f, 0f));

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
