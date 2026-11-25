using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18: a runtime hue/saturation wheel plus a brightness slider and a
    /// preview swatch - UI Toolkit ships no built-in runtime color picker
    /// (ColorField is editor-only), so this draws its own wheel onto a
    /// small generated texture and reads hue/saturation back from where
    /// the pointer lands on it. Brightness is a plain UI Toolkit Slider.
    /// </summary>
    public sealed class ColorWheelPicker
    {
        private const float WheelSizePixels = 140f;
        private const int WheelTextureResolution = 128;
        private const float MarkerSizePixels = 10f;

        private readonly Action<Color> _onChanged;

        private VisualElement _root;
        private VisualElement _wheel;
        private VisualElement _marker;
        private Slider _brightnessSlider;
        private VisualElement _swatch;

        private float _hue;
        private float _saturation;
        private float _brightness = 1f;

        public VisualElement Root => _root;
        public Color Value => Color.HSVToRGB(_hue, _saturation, _brightness);

        public ColorWheelPicker(Action<Color> onChanged)
        {
            _onChanged = onChanged;
            Build();
        }

        /// <summary>Sets the picker to this color without firing onChanged - for defaulting to a zone type's own tint.</summary>
        public void SetValue(Color color)
        {
            Color.RGBToHSV(color, out _hue, out _saturation, out _brightness);
            _brightnessSlider.SetValueWithoutNotify(_brightness);
            RefreshMarker();
            RefreshSwatch();
        }

        private void Build()
        {
            _root = new VisualElement { style = { flexDirection = FlexDirection.Column, alignItems = Align.Center } };

            _wheel = new VisualElement
            {
                style =
                {
                    width = WheelSizePixels,
                    height = WheelSizePixels,
                    marginBottom = 8,
                    backgroundImage = new StyleBackground(Background.FromTexture2D(BuildWheelTexture(WheelTextureResolution)))
                }
            };
            _wheel.RegisterCallback<PointerDownEvent>(OnWheelPointerDown);
            _wheel.RegisterCallback<PointerMoveEvent>(OnWheelPointerMove);
            _wheel.RegisterCallback<PointerUpEvent>(OnWheelPointerUp);

            _marker = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    width = MarkerSizePixels,
                    height = MarkerSizePixels,
                    borderTopLeftRadius = MarkerSizePixels * 0.5f,
                    borderTopRightRadius = MarkerSizePixels * 0.5f,
                    borderBottomLeftRadius = MarkerSizePixels * 0.5f,
                    borderBottomRightRadius = MarkerSizePixels * 0.5f,
                    backgroundColor = Color.white,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = Color.black,
                    borderRightColor = Color.black,
                    borderTopColor = Color.black,
                    borderBottomColor = Color.black
                }
            };
            _wheel.Add(_marker);
            _root.Add(_wheel);

            _brightnessSlider = new Slider(0f, 1f) { value = _brightness, style = { width = WheelSizePixels, marginBottom = 8 } };
            _brightnessSlider.RegisterValueChangedCallback(OnBrightnessChanged);
            _root.Add(_brightnessSlider);

            _swatch = new VisualElement
            {
                style =
                {
                    width = 48,
                    height = 24,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = Color.black,
                    borderRightColor = Color.black,
                    borderTopColor = Color.black,
                    borderBottomColor = Color.black
                }
            };
            _root.Add(_swatch);

            RefreshMarker();
            RefreshSwatch();
        }

        private void OnBrightnessChanged(ChangeEvent<float> evt)
        {
            _brightness = evt.newValue;
            RefreshSwatch();
            _onChanged?.Invoke(Value);
        }

        private void OnWheelPointerDown(PointerDownEvent evt)
        {
            _wheel.CapturePointer(evt.pointerId);
            UpdateFromLocalPosition(evt.localPosition);
        }

        private void OnWheelPointerMove(PointerMoveEvent evt)
        {
            if (_wheel.HasPointerCapture(evt.pointerId))
            {
                UpdateFromLocalPosition(evt.localPosition);
            }
        }

        private void OnWheelPointerUp(PointerUpEvent evt)
        {
            if (_wheel.HasPointerCapture(evt.pointerId))
            {
                _wheel.ReleasePointer(evt.pointerId);
            }
        }

        private void UpdateFromLocalPosition(Vector2 localPosition)
        {
            float radius = WheelSizePixels * 0.5f;
            Vector2 offset = localPosition - new Vector2(radius, radius);
            float distance = offset.magnitude;

            _saturation = Mathf.Clamp01(distance / radius);
            _hue = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) / (2f * Mathf.PI), 1f);

            RefreshMarker();
            RefreshSwatch();
            _onChanged?.Invoke(Value);
        }

        private void RefreshMarker()
        {
            float radius = WheelSizePixels * 0.5f;
            float angleRadians = _hue * 2f * Mathf.PI;
            float distance = _saturation * radius;

            float x = radius + Mathf.Cos(angleRadians) * distance - MarkerSizePixels * 0.5f;
            float y = radius + Mathf.Sin(angleRadians) * distance - MarkerSizePixels * 0.5f;

            _marker.style.left = x;
            _marker.style.top = y;
        }

        private void RefreshSwatch()
        {
            _swatch.style.backgroundColor = Value;
        }

        /// <summary>A circular hue (angle) / saturation (distance from center) wheel at full brightness; fully transparent outside the circle.</summary>
        private static Texture2D BuildWheelTexture(int resolution)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false)
            {
                name = "ColorWheelTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float radius = resolution * 0.5f;
            var pixels = new Color32[resolution * resolution];

            for (int y = 0; y < resolution; y++)
            {
                float dy = y + 0.5f - radius;
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x + 0.5f - radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    int index = y * resolution + x;

                    if (distance > radius)
                    {
                        pixels[index] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float hue = Mathf.Repeat(Mathf.Atan2(dy, dx) / (2f * Mathf.PI), 1f);
                    float saturation = Mathf.Clamp01(distance / radius);
                    pixels[index] = Color.HSVToRGB(hue, saturation, 1f);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
