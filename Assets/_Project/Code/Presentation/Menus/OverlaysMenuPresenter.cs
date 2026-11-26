using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Menus
{
    /// <summary>
    /// R18b: the HUD "Overlays" button's small popup - for now, just a
    /// "Toggle zones" checkbox that shows or hides every placed zone's
    /// tint and outline. Plain UI Toolkit code, same style as the HUD.
    /// </summary>
    public sealed class OverlaysMenuPresenter
    {
        private VisualElement _panel;

        public bool IsOpen => _panel != null && _panel.style.display == DisplayStyle.Flex;

        /// <summary>R18e: hudPanel is the top HUD bar this popup must open just below, never over - its height changes with the UI size setting, so the gap is measured from its actual layout (recomputed live) rather than a fixed guess.</summary>
        public void Initialize(VisualElement uiRoot, VisualElement hudPanel, bool initialZonesVisible, Action<bool> onZonesVisibleChanged)
        {
            _panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 8,
                    paddingLeft = 12,
                    paddingRight = 12,
                    paddingTop = 8,
                    paddingBottom = 8,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f),
                    display = DisplayStyle.None
                }
            };

            const float gapBelowHud = 4f;
            hudPanel.RegisterCallback<GeometryChangedEvent>(_ => _panel.style.top = hudPanel.layout.yMax + gapBelowHud);

            var zonesToggle = new Toggle("Toggle zones") { value = initialZonesVisible };
            zonesToggle.RegisterValueChangedCallback(evt => onZonesVisibleChanged?.Invoke(evt.newValue));
            _panel.Add(zonesToggle);

            uiRoot.Add(_panel);
        }

        public void Toggle()
        {
            _panel.style.display = IsOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void Close()
        {
            _panel.style.display = DisplayStyle.None;
        }
    }
}
