using UnityEngine;
using UnityEngine.Rendering;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// TEMPORARY (R1): a flat placeholder ground with a faint grid, so the
    /// camera has something to look at before the real map system exists.
    /// Replace this whole class when the map module and its presenter are
    /// built.
    /// </summary>
    public static class TemporaryGround
    {
        public const int WidthCells = 256;
        public const int DepthCells = 256;
        public const float CellSizeMeters = 1f;

        public const float WidthMeters = WidthCells * CellSizeMeters;
        public const float DepthMeters = DepthCells * CellSizeMeters;

        public static GameObject Build()
        {
            var ground = new GameObject("TemporaryGround (placeholder - replace with the map system)");

            var meshFilter = ground.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildMesh();

            var meshRenderer = ground.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = BuildMaterial();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            return ground;
        }

        private static Mesh BuildMesh()
        {
            var mesh = new Mesh { name = "TemporaryGroundMesh" };

            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, DepthMeters),
                new Vector3(WidthMeters, 0f, DepthMeters),
                new Vector3(WidthMeters, 0f, 0f)
            };

            var uvs = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };

            var triangles = new[] { 0, 1, 2, 0, 2, 3 };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>An unlit material with a small generated grid texture, tiled once per cell.</summary>
        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = "TemporaryGroundMaterial" };

            Texture2D texture = BuildGridTexture();
            material.mainTexture = texture;
            material.mainTextureScale = new Vector2(WidthCells, DepthCells);

            return material;
        }

        private static Texture2D BuildGridTexture()
        {
            const int size = 8;
            var groundColor = new Color(0.33f, 0.36f, 0.33f);
            var lineColor = new Color(0.27f, 0.3f, 0.27f);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "TemporaryGroundGridTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onLine = x == 0 || y == 0;
                    texture.SetPixel(x, y, onLine ? lineColor : groundColor);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
