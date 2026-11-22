using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>Screen-to-panel pointer and focus queries shared by anything that needs to tell a UI interaction from a world one.</summary>
    public static class UiInputUtility
    {
        /// <summary>Whether a screen position lands on a visible UI element rather than the world behind it.</summary>
        public static bool IsPointerOverUi(VisualElement uiRoot, Vector2 screenPosition)
        {
            if (uiRoot?.panel == null)
            {
                return false;
            }

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(uiRoot.panel, screenPosition);
            return uiRoot.panel.Pick(panelPosition) != null;
        }

        /// <summary>Whether the currently focused UI control is a text field actively accepting typed input - gameplay keys must yield to it.</summary>
        public static bool IsTextFieldFocused(VisualElement uiRoot)
        {
            if (!(uiRoot?.panel?.focusController?.focusedElement is VisualElement focused))
            {
                return false;
            }

            return focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null;
        }
    }
}
