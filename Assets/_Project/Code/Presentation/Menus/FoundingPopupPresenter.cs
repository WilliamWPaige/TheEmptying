using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Menus
{
    /// <summary>
    /// R11: "Found your kingdom here?" - opened by clicking the starting
    /// site marker. No closes it; Escape closes it the same as No (fits
    /// the existing Escape priority in GameBootstrap). An ordinary UI
    /// Toolkit panel, so clicks on it are already excluded from world
    /// clicks by the usual IsPointerOverUi check.
    /// </summary>
    public sealed class FoundingPopupPresenter
    {
        private VisualElement _root;

        public bool IsOpen => _root != null && _root.style.display == DisplayStyle.Flex;

        public void Initialize(VisualElement uiRoot, Action onYes)
        {
            _root = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    display = DisplayStyle.None
                }
            };

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
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f)
                }
            };

            panel.Add(new Label("Found your kingdom here?")
            {
                style = { color = Color.white, fontSize = 18, marginBottom = 16, whiteSpace = WhiteSpace.Normal, maxWidth = 320 }
            });

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttonRow.Add(new Button(() =>
            {
                Close();
                onYes?.Invoke();
            })
            { text = "Yes", style = { width = 100, height = 36, marginRight = 8 } });
            buttonRow.Add(new Button(Close) { text = "No", style = { width = 100, height = 36 } });
            panel.Add(buttonRow);

            _root.Add(panel);
            uiRoot.Add(_root);
        }

        public void Open()
        {
            _root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            _root.style.display = DisplayStyle.None;
        }

        /// <summary>Escape's entry point - closes this the same as clicking No.</summary>
        public void HandleEscapePressed()
        {
            Close();
        }
    }
}
