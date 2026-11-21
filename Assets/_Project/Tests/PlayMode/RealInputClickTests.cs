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
    /// 3.9c/3.9d: a click on any build-menu button must never also reach the world behind it.
    ///
    /// UI Toolkit's own button click dispatch can't be driven through InputTestFixture's simulated
    /// Mouse device: confirmed empirically (DIAG trace from this prompt's own investigation) that the
    /// device's position and button-press state update correctly and are visible to Mouse.current
    /// (so BuildingsPresenter's own polling code, which is what actually matters here, sees them
    /// fine), but UI Toolkit's internal pointer-event pipeline never turns a simulated press+release
    /// over a Button into its "clicked" event, even after 60 frames - so OpenBuildMenuWithTypeSelectedForTesting
    /// is used to reach "menu open, a type selected", and every assertion below is driven by a real
    /// simulated hardware click through BuildingsPresenter's actual Update()/Mouse.current polling and
    /// UiInputUtility.IsPointerOverUi check - the exact mechanism the bug report is about.
    /// </summary>
    public class RealInputClickTests : InputTestFixture
    {
        private const string TypeA = "building.crown_lodging";
        private const string TypeB = "building.crown_storehouse";

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
        public IEnumerator RealClicksOnMenuButtons_NeverPlaceABuildingInTheWorld()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(5, 5)), "Setup: placing the stockpile should succeed.");
            yield return null;

            string buildingsBefore = bootstrap.RunConsoleCommandForTesting("buildings");
            int countBefore = CountBuildingLines(buildingsBefore);

            Vector2? buttonPanelPos = bootstrap.BuildMenuToggleButtonPositionForTesting;
            Assert.IsTrue(buttonPanelPos.HasValue, "Expected the build menu's toggle button to exist.");
            Vector2 buttonScreenPos = bootstrap.ScreenPositionForPanelPointForTesting(buttonPanelPos.Value).Value;

            // Reach "tool active, a type selected" directly - see the class comment for why a real
            // click can't open the menu in this harness. Every click below, the thing actually under
            // test, is real.
            bootstrap.OpenBuildMenuWithTypeSelectedForTesting(TypeA);
            yield return null;

            Vector2? typeBPanelPos = bootstrap.MenuTypeButtonPositionForTesting(TypeB);
            Assert.IsTrue(typeBPanelPos.HasValue, $"Expected a {TypeB} button in the open build menu.");
            Vector2 typeBScreenPos = bootstrap.ScreenPositionForPanelPointForTesting(typeBPanelPos.Value).Value;

            // Real click on a different type's button in the menu, with the tool already active and a
            // type already selected - the exact scenario the hand test found broken.
            Assert.IsTrue(bootstrap.IsPointerOverUiForTesting(typeBScreenPos), $"Sanity check: {TypeB}'s button position should register as over UI.");
            yield return Click(mouse, typeBScreenPos);

            string buildingsAfterTypeBClick = bootstrap.RunConsoleCommandForTesting("buildings");
            Assert.AreEqual(countBefore, CountBuildingLines(buildingsAfterTypeBClick),
                "A real click on a type button in the menu should never place a building behind it.");

            // Real click on the Build toggle button itself, still with a type selected - the other half
            // of the scenario the hand test found broken.
            Assert.IsTrue(bootstrap.IsPointerOverUiForTesting(buttonScreenPos), "Sanity check: the toggle button's position should register as over UI.");
            yield return Click(mouse, buttonScreenPos);

            string buildingsAfter = bootstrap.RunConsoleCommandForTesting("buildings");
            Assert.AreEqual(countBefore, CountBuildingLines(buildingsAfter),
                "A real click on the build menu's own toggle button should never place a building behind it.");
        }
    }
}
