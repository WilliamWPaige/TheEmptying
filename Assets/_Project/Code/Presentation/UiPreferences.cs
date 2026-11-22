using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>Named, marked-placeholder UI scale choices. The percentages are not final art sizing.</summary>
    public enum UiSize
    {
        Small,
        Normal,
        Large
    }

    /// <summary>
    /// The player's UI size choice: a local preference (PlayerPrefs), never
    /// game data - it is not saved in a save file and never appears in the
    /// cheat menu's game settings. Applied by scaling the one PanelSettings
    /// asset every scene's UI shares, so a single RegisterPanel call per
    /// scene covers the main menu, HUD, escape menu, options and the debug
    /// window at once, with no restart needed when it changes.
    /// </summary>
    public static class UiPreferences
    {
        private const string PrefsKey = "ui.size";

        private static PanelSettings _trackedPanelSettings;

        public static UiSize CurrentSize { get; private set; } = LoadSaved();

        /// <summary>Raised after CurrentSize changes, so an open Options panel can update which choice looks selected.</summary>
        public static event Action Changed;

        public static float ScaleFactorFor(UiSize size)
        {
            switch (size)
            {
                case UiSize.Small: return 1.00f;
                case UiSize.Normal: return 1.25f;
                case UiSize.Large: return 1.50f;
                default: return 1.00f;
            }
        }

        public static string LabelFor(UiSize size)
        {
            switch (size)
            {
                case UiSize.Small: return "Small (100%)";
                case UiSize.Normal: return "Normal (125%)";
                case UiSize.Large: return "Large (150%)";
                default: return size.ToString();
            }
        }

        public static void SetSize(UiSize size)
        {
            if (size == CurrentSize)
            {
                return;
            }

            CurrentSize = size;
            PlayerPrefs.SetInt(PrefsKey, (int)size);
            PlayerPrefs.Save();
            ApplyToTrackedPanel();
            Changed?.Invoke();
        }

        /// <summary>
        /// Called by each scene's bootstrap right after it loads the shared
        /// GamePanelSettings asset, so the saved preference is applied with
        /// no restart needed, however many scenes load it and in whatever
        /// order.
        /// </summary>
        public static void RegisterPanel(PanelSettings panelSettings)
        {
            _trackedPanelSettings = panelSettings;
            ApplyToTrackedPanel();
        }

        private static void ApplyToTrackedPanel()
        {
            if (_trackedPanelSettings != null)
            {
                _trackedPanelSettings.scale = ScaleFactorFor(CurrentSize);
            }
        }

        private static UiSize LoadSaved()
        {
            int raw = PlayerPrefs.GetInt(PrefsKey, (int)UiSize.Normal);
            return Enum.IsDefined(typeof(UiSize), raw) ? (UiSize)raw : UiSize.Normal;
        }
    }
}
