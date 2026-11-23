using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R12/R12b: a tinted, outlined rectangle drawn flat on the ground -
    /// shared by the live drawing preview and every placed zone. One
    /// quad per zone: the "outline" comes from a shared, color-neutral
    /// generated texture (a darker border baked around a lighter fill),
    /// tinted per instance through the material's color (so SetColor -
    /// used live while dragging, and to brighten a selected zone's
    /// outline - never touches a texture).
    /// </summary>
    public sealed class ZoneVisual
    {
        private static Texture2D s_sharedTexture;

        private readonly GameObject _quad;
        private readonly Material _material;

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
            _material = BuildMaterial();
            renderer.material = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            SetColor(color);
        }

        public void SetActive(bool active) => _quad.SetActive(active);

        public void Destroy() => Object.Destroy(_quad);

        /// <summary>The texture is color-neutral (gray fill, darker gray border); this tint multiplies both at once, so changing color is just a material property - cheap enough to call every frame while dragging.</summary>
        public void SetColor(Color color) => _material.color = color;

        /// <summary>Positions, sizes and rotates the quad to match this rectangle, through the floating origin.</summary>
        public void UpdateGeometry(OrientedRect rect, FloatingOrigin floatingOrigin)
        {
            var worldCenter = new Vector3((float)rect.CenterX, 0.02f, (float)rect.CenterZ);
            Vector3 renderCenter = floatingOrigin.WorldToRender(worldCenter);

            _quad.transform.position = renderCenter;
            _quad.transform.rotation = Quaternion.Euler(90f, (float)-rect.RotationDegrees, 0f);
            _quad.transform.localScale = new Vector3((float)rect.SideALength, (float)rect.SideBLength, 1f);
        }

        /// <summary>Opaque by design - no shader transparency setup to get wrong. Per-instance (not shared) so SetColor never bleeds across zones.</summary>
        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = "ZoneMaterial" };
            material.mainTexture = GetSharedTexture();
            return material;
        }

        private static Texture2D GetSharedTexture()
        {
            if (s_sharedTexture != null)
            {
                return s_sharedTexture;
            }

            const int size = 32;
            const int borderThickness = 3;

            var fill = new Color(0.72f, 0.72f, 0.72f);
            var border = new Color(0.3f, 0.3f, 0.3f);

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
            s_sharedTexture = texture;
            return texture;
        }
    }
}
