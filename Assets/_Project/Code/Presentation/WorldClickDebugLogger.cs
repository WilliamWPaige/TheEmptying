using TheEmptying.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>
    /// 3.9e: behind setting.debug.log_world_clicks, logs every left mouse press and release with the
    /// screen position, whether UiInputUtility.IsPointerOverUi caught it, which UI element (if any)
    /// RuntimePanelUtils/panel.Pick actually found there, and what world action (if any) the caller
    /// took - so a click-through bug can be diagnosed from the log instead of guessed at.
    /// </summary>
    public static class WorldClickDebugLogger
    {
        public const string EnabledSettingId = "setting.debug.log_world_clicks";

        public static void LogPress(SettingsRegistry settings, VisualElement uiRoot, Vector2 screenPos, string action) =>
            Log(settings, uiRoot, screenPos, "press", action);

        public static void LogRelease(SettingsRegistry settings, VisualElement uiRoot, Vector2 screenPos, string action) =>
            Log(settings, uiRoot, screenPos, "release", action);

        private static void Log(SettingsRegistry settings, VisualElement uiRoot, Vector2 screenPos, string phase, string action)
        {
            if (settings == null || settings.Get(EnabledSettingId) == 0)
            {
                return;
            }

            bool overUi = UiInputUtility.IsPointerOverUi(uiRoot, screenPos);
            string picked = DescribePickedElement(uiRoot, screenPos);
            Debug.Log($"[WorldClick] {phase} at {screenPos}: overUi={overUi} picked={picked} action={action}");
        }

        private static string DescribePickedElement(VisualElement uiRoot, Vector2 screenPos)
        {
            if (uiRoot?.panel == null)
            {
                return "none (no panel)";
            }

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(uiRoot.panel, screenPos);
            VisualElement picked = uiRoot.panel.Pick(panelPos);
            if (picked == null)
            {
                return "none";
            }

            string name = string.IsNullOrEmpty(picked.name) ? "(unnamed)" : picked.name;
            return $"{picked.GetType().Name}#{name} pickingMode={picked.pickingMode}";
        }
    }
}
