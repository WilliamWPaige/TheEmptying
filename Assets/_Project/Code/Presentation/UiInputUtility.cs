using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>Shared "is the pointer over a UI panel right now" check, so a click meant for the build menu, a popup or the console never also reaches the 3D world underneath it.</summary>
    public static class UiInputUtility
    {
        public static bool IsPointerOverUi(VisualElement uiRoot, Vector2 screenPosition)
        {
            if (uiRoot?.panel == null)
            {
                return false;
            }

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(uiRoot.panel, screenPosition);
            return uiRoot.panel.Pick(panelPosition) != null;
        }
    }
}
