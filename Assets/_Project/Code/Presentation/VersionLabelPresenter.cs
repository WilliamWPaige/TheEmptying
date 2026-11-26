using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation
{
    /// <summary>
    /// R19: the small, faint build version shown in the bottom-right corner,
    /// in game and on the main menu - reads Application.version, so Project
    /// Settings &gt; Player &gt; Version is the only place the version itself
    /// is stored. Picking is off, so it never blocks a click, and it's added
    /// once, directly under the shared UI root, so nothing built later (such
    /// as the zoning view) covers that corner.
    /// </summary>
    public static class VersionLabelPresenter
    {
        public static void Build(VisualElement uiRoot)
        {
            var label = new Label($"v{Application.version}")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    right = 6,
                    bottom = 4,
                    color = new Color(1f, 1f, 1f, 0.35f),
                    fontSize = 11
                }
            };

            uiRoot.Add(label);
        }
    }
}
