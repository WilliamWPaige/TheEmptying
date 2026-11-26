using NUnit.Framework;
using TheEmptying.Presentation;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.Presentation
{
    /// <summary>R18g: the screen-to-panel y flip PickOverUi applies - Input System screen positions start at the bottom-left (y grows upward); UI Toolkit panel positions start at the top-left (y grows downward).</summary>
    public class UiInputUtilityTests
    {
        [TestCase(1920f, 1080f, 1000f, 1f)]
        [TestCase(1920f, 1080f, 1030f, 1.25f)]
        [TestCase(1280f, 720f, 700f, 1.5f)]
        public void FlipScreenY_ThenScaleByUiSize_MousePositionNearTopOfScreen_ConvertsNearTopOfPanel(
            float screenWidth, float screenHeight, float mouseYNearTopOfScreen, float uiScale)
        {
            var screenPosition = new Vector2(screenWidth * 0.5f, mouseYNearTopOfScreen);

            Vector2 flipped = UiInputUtility.FlipScreenY(screenPosition, screenHeight);
            Vector2 panelPosition = flipped / uiScale;

            float panelHeight = screenHeight / uiScale;
            Assert.Less(panelPosition.y, panelHeight * 0.1f, "A mouse position near the top of the screen should convert to a position near the top of the panel (a small y), not the bottom.");
        }

        [TestCase(1920f, 1080f, 50f, 1f)]
        [TestCase(1920f, 1080f, 20f, 1.25f)]
        public void FlipScreenY_ThenScaleByUiSize_MousePositionNearBottomOfScreen_ConvertsNearBottomOfPanel(
            float screenWidth, float screenHeight, float mouseYNearBottomOfScreen, float uiScale)
        {
            var screenPosition = new Vector2(screenWidth * 0.5f, mouseYNearBottomOfScreen);

            Vector2 flipped = UiInputUtility.FlipScreenY(screenPosition, screenHeight);
            Vector2 panelPosition = flipped / uiScale;

            float panelHeight = screenHeight / uiScale;
            Assert.Greater(panelPosition.y, panelHeight * 0.9f, "A mouse position near the bottom of the screen should convert to a position near the bottom of the panel (close to its full height), not the top.");
        }
    }
}
