using System.Collections;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>
    /// 3.9c: the 3.9b UI-click-blocking test only ever drove the test helper, never a real mouse
    /// through the Input System, so it couldn't catch a bug specific to the real input path. This
    /// drives an actual simulated Mouse device (via InputTestFixture) through the same clicks a
    /// player would make with the keyboard and mouse.
    /// </summary>
    public class RealInputClickTests : InputTestFixture
    {
        private static int CountBuildingLines(string buildingsCommandOutput) =>
            buildingsCommandOutput.Trim() == "No buildings." ? 0 : buildingsCommandOutput.Split('\n').Length;

        private IEnumerator Click(Mouse mouse, Vector2 screenPosition)
        {
            Move(mouse.position, screenPosition);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickingTheBuildButtonAgain_ThroughRealInput_NeverPlacesABuildingBehindIt()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(5, 5)), "Setup: placing the stockpile should succeed.");
            yield return null;

            Vector2? buttonPanelPos = bootstrap.BuildMenuToggleButtonPositionForTesting;
            Assert.IsTrue(buttonPanelPos.HasValue, "Expected the build menu's toggle button to exist.");
            Vector2? buttonScreenPosNullable = bootstrap.ScreenPositionForPanelPointForTesting(buttonPanelPos.Value);
            Assert.IsTrue(buttonScreenPosNullable.HasValue);
            Vector2 buttonScreenPos = buttonScreenPosNullable.Value;

            string buildingsBefore = bootstrap.RunConsoleCommandForTesting("buildings");
            int countBefore = CountBuildingLines(buildingsBefore);

            // Set up "tool active, a type already selected" directly - getting there isn't what's in
            // question here, and chasing the menu's own type-button layout timing (its worldBound
            // isn't available for several frames after the menu first becomes visible) would only make
            // this test about UI Toolkit's layout pass instead of the actual click being tested below.
            bootstrap.OpenBuildMenuWithTypeSelectedForTesting("building.crown_lodging");
            yield return null;

            // The real click in question: the scenario the hand test found broken - click the Build
            // button again (closing the tool) while the mouse is still over it, through the actual
            // Input System and BuildingsPresenter's real Update() polling, not a test shortcut. That
            // single click must only close the menu, never also place a building in the world behind it.
            yield return Click(mouse, buttonScreenPos);

            string buildingsAfter = bootstrap.RunConsoleCommandForTesting("buildings");
            Assert.AreEqual(countBefore, CountBuildingLines(buildingsAfter),
                "Clicking the build menu's own button a second time should never place a building behind it.");
        }
    }
}
