using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>Screen-to-panel pointer and focus queries shared by anything that needs to tell a UI interaction from a world one.</summary>
    public static class UiInputUtility
    {
        /// <summary>Whether a screen position lands on a visible UI element rather than the world behind it.</summary>
        public static bool IsPointerOverUi(VisualElement uiRoot, Vector2 screenPosition) => PickOverUi(uiRoot, screenPosition) != null;

        /// <summary>
        /// R18c: the actual UI Toolkit element a screen position picks, or null if it lands on
        /// the world behind every UI element - for callers (like the zoning paint debug log)
        /// that need to say which element caught a click, not just whether one did. A plain
        /// VisualElement with no background still counts as a hit as long as its pickingMode
        /// is Position (the default) and it isn't display: none - a layout-only full-screen
        /// container must set pickingMode to Ignore, or it catches every click meant for the
        /// world behind it (see ZoningMenuPresenter's own root, fixed in R18c).
        /// </summary>
        public static VisualElement PickOverUi(VisualElement uiRoot, Vector2 screenPosition)
        {
            if (uiRoot?.panel == null)
            {
                return null;
            }

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(uiRoot.panel, screenPosition);
            return uiRoot.panel.Pick(panelPosition);
        }

        /// <summary>
        /// Whether the currently focused UI control is a text field actively
        /// accepting typed input - gameplay keys must yield to it. Every
        /// text-entry control (TextField, IntegerField, FloatField and so
        /// on) shares the same inner focusable element, named
        /// "unity-text-input", regardless of the field's own value type.
        ///
        /// The focus controller can keep pointing at an element after it
        /// (or an ancestor) has been removed or hidden - most commonly a
        /// field destroyed by its own panel rebuilding itself while still
        /// holding focus. Such a stale reference never counts as focused.
        /// </summary>
        public static bool IsTextFieldFocused(VisualElement uiRoot)
        {
            Focusable focused = uiRoot?.panel?.focusController?.focusedElement;
            if (!(focused is VisualElement element) || element.name != "unity-text-input")
            {
                return false;
            }

            return IsAttachedAndVisible(element);
        }

        /// <summary>Still part of the panel's tree (not removed), and no ancestor up to the root is display: none (not hidden, and not inside a closed window).</summary>
        private static bool IsAttachedAndVisible(VisualElement element)
        {
            if (element.panel == null)
            {
                return false;
            }

            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
