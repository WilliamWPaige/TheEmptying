using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R12: a tinted, outlined rectangle drawn flat on the ground - shared
    /// by the live drawing preview and every placed zone. One quad per
    /// zone: the "outline" comes from a small generated texture (a
    /// darker border baked around a lighter fill), not separate edge
    /// geometry, so updating a zone's shape is just one transform.
    /// </summary>
    public sealed class ZoneVisual
    {
        private readonly GameObject _quad;

        public GameObject GameObject => _quad;

        public ZoneVisual(Transform parent, Color color, bool collidable)
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "Zone";
            _quad.transform.SetParent(parent, worldPositionStays: true);
            _quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (!collidable)
            {
                Collider collider = _quad.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.Destroy(collider);
                }
            }

            var renderer = _quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = BuildMaterial(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetActive(bool active) => _quad.SetActive(active);

        public void Destroy() => Object.Destroy(_quad);

        /// <summary>Positions, sizes and rotates the quad to match this rectangle, through the floating origin.</summary>
        public void UpdateGeometry(OrientedRect rect, FloatingOrigin floatingOrigin)
        {
            var worldCenter = new Vector3((float)rect.CenterX, 0.02f, (float)rect.CenterZ);
            Vector3 renderCenter = floatingOrigin.WorldToRender(worldCenter);

            _quad.transform.position = renderCenter;
            _quad.transform.rotation = Quaternion.Euler(90f, (float)-rect.RotationDegrees, 0f);
            _quad.transform.localScale = new Vector3((float)rect.SideALength, (float)rect.SideBLength, 1f);
        }

        /// <summary>Opaque by design - a lighter, muted fill with a darker, more saturated border; no shader transparency setup to get wrong.</summary>
        private static Material BuildMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = "ZoneMaterial" };
            material.mainTexture = BuildTexture(color);
            return material;
        }

        private static Texture2D BuildTexture(Color color)
        {
            const int size = 32;
            const int borderThickness = 3;

            Color fill = Color.Lerp(color, Color.white, 0.65f);
            Color border = Color.Lerp(color, Color.black, 0.35f);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "ZoneTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onBorder = x < borderThickness || y < borderThickness || x >= size - borderThickness || y >= size - borderThickness;
                    texture.SetPixel(x, y, onBorder ? border : fill);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
