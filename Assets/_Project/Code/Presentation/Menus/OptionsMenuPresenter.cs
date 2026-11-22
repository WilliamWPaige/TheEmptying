using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Menus
{
    /// <summary>
    /// The one shared Options panel - built once here and placed into both
    /// the main menu and the in-game Escape menu, so they always match.
    /// Plain UI Toolkit code, same style as the HUD and main menu.
    /// </summary>
    public sealed class OptionsMenuPresenter
    {
        private VisualElement _panel;
        private readonly Dictionary<UiSize, Button> _sizeButtons = new Dictionary<UiSize, Button>();

        public VisualElement Panel => _panel;
        public bool IsOpen => _panel.style.display == DisplayStyle.Flex;

        public void Initialize(Action onBack)
        {
            _panel = BuildPanel(onBack);
            _panel.style.display = DisplayStyle.None;
            UiPreferences.Changed += RefreshSizeButtons;
        }

        /// <summary>Unsubscribes from UiPreferences.Changed. Call from the owner's OnDestroy so a scene unload doesn't leak this into the next scene's presenter.</summary>
        public void Dispose()
        {
            UiPreferences.Changed -= RefreshSizeButtons;
        }

        public void Open()
        {
            RefreshSizeButtons();
            _panel.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            _panel.style.display = DisplayStyle.None;
        }

        private VisualElement BuildPanel(Action onBack)
        {
            var panel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Center,
                    paddingLeft = 32,
                    paddingRight = 32,
                    paddingTop = 24,
                    paddingBottom = 24,
                    backgroundColor = new Color(0f, 0f, 0f, 0.5f)
                }
            };

            var title = new Label("Options")
            {
                style =
                {
                    color = Color.white,
                    fontSize = 28,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 16
                }
            };
            panel.Add(title);

            foreach (UiSize size in (UiSize[])Enum.GetValues(typeof(UiSize)))
            {
                UiSize capturedSize = size;
                var button = new Button(() => UiPreferences.SetSize(capturedSize))
                {
                    style = { width = 220, height = 32, marginBottom = 6 }
                };
                _sizeButtons[capturedSize] = button;
                panel.Add(button);
            }

            var back = new Button(onBack) { text = "Back", style = { width = 220, height = 32, marginTop = 10 } };
            panel.Add(back);

            RefreshSizeButtons();
            return panel;
        }

        private void RefreshSizeButtons()
        {
            foreach (KeyValuePair<UiSize, Button> entry in _sizeButtons)
            {
                bool selected = entry.Key == UiPreferences.CurrentSize;
                entry.Value.text = selected ? $"> {UiPreferences.LabelFor(entry.Key)}" : UiPreferences.LabelFor(entry.Key);
            }
        }
    }
}
