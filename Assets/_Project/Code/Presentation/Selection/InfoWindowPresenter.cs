using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>
    /// R8: one window docked on the right, showing whatever was last
    /// clicked on the map. Built once; its body is rebuilt from the
    /// current ISelectionTarget each time the selection changes.
    /// </summary>
    public sealed class InfoWindowPresenter
    {
        private VisualElement _root;
        private VisualElement _content;

        public bool IsOpen => _root != null && _root.style.display == DisplayStyle.Flex;

        public void Initialize(VisualElement uiRoot, Action onCenterCamera, Action onClose)
        {
            _root = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 0,
                    bottom = 0,
                    right = 0,
                    width = 300,
                    backgroundColor = new Color(0f, 0f, 0f, 0.65f),
                    paddingLeft = 12,
                    paddingRight = 12,
                    paddingTop = 8,
                    paddingBottom = 12,
                    display = DisplayStyle.None
                }
            };

            var headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginBottom = 4 } };
            headerRow.Add(new Button(onClose) { text = "X", style = { width = 26, height = 26 } });
            _root.Add(headerRow);

            var scrollView = new ScrollView { style = { flexGrow = 1 } };
            _content = scrollView.contentContainer;
            _root.Add(scrollView);

            _root.Add(new Button(onCenterCamera) { text = "Center camera", style = { marginTop = 8 } });

            uiRoot.Add(_root);
        }

        public void Show(ISelectionTarget target)
        {
            _content.Clear();
            target.BuildContent(_content);
            _root.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
            _content.Clear();
        }
    }
}
