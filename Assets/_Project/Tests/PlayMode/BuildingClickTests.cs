using System.Collections;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>
    /// 3.9b's two UI fixes: a click over a UI panel never also reaches the
    /// 3D world behind it, and clicking a construction site (not just a
    /// finished building) opens the building panel.
    /// </summary>
    public class BuildingClickTests
    {
        private static int CountBuildingLines(string buildingsCommandOutput) =>
            buildingsCommandOutput.Trim() == "No buildings." ? 0 : buildingsCommandOutput.Split('\n').Length;

        [UnityTest]
        public IEnumerator ClickingOverTheBuildMenuButton_PlacesNothingBehindIt()
        {
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
            Assert.IsTrue(bootstrap.IsPointerOverUiForTesting(buttonScreenPos), "Sanity check: the computed screen position should actually be over the button.");

            string buildingsBefore = bootstrap.RunConsoleCommandForTesting("buildings");
            int countBefore = CountBuildingLines(buildingsBefore);

            // Crown Lodging at (20,20) doesn't overlap the stockpile at (5,5), so this click would
            // place something if the UI-block check weren't actually stopping it.
            EntityId? placed = bootstrap.SimulatePlacementClickForTesting("building.crown_lodging", new GridPoint(20, 20), buttonScreenPos);

            Assert.IsNull(placed, "A click over the build menu's own button should never place a building behind it.");

            string buildingsAfter = bootstrap.RunConsoleCommandForTesting("buildings");
            Assert.AreEqual(countBefore, CountBuildingLines(buildingsAfter), "No new building should have been placed.");
        }

        [UnityTest]
        public IEnumerator ClickingAConstructionSite_OpensTheBuildingPanel()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            // Near the map's center (the default 256x256 map, where the strategy camera starts
            // looking), so the site is actually within the camera's view for WorldToScreenPoint below.
            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            // Bottom-right corner: away from the HUD, the build menu and the console, all anchored near the top.
            var farCorner = new Vector2(Screen.width - 5f, 5f);
            EntityId? siteId = bootstrap.SimulatePlacementClickForTesting("building.crown_lodging", new GridPoint(115, 115), farCorner);
            Assert.IsTrue(siteId.HasValue, "Setup: placing the crown lodging site away from any UI should succeed.");

            // Close the build tool first: a real player would click "Build" again (or right-click) before
            // trying to inspect anything, and the tool being active is exactly what makes a world click place
            // instead of inspect.
            bootstrap.CloseBuildMenuForTesting();

            yield return null; // let the site's cube actually appear this frame
            yield return null;

            double cellSize = bootstrap.CellSizeMetersForTesting;
            var worldCenter = new Vector3((float)((115 + 4 * 0.5) * cellSize), 1f, (float)((115 + 4 * 0.5) * cellSize)); // Crown Lodging's 4x4 footprint, centered

            Camera camera = bootstrap.CameraForTesting;
            Assert.IsNotNull(camera, "Expected a main camera.");
            Vector3 screenPoint = camera.WorldToScreenPoint(worldCenter);
            Assert.Greater(screenPoint.z, 0f, "Sanity check: the site should be in front of the camera.");
            Assert.That(screenPoint.x, Is.InRange(0f, Screen.width), "Sanity check: the site's screen X should be on-screen.");
            Assert.That(screenPoint.y, Is.InRange(0f, Screen.height), "Sanity check: the site's screen Y should be on-screen.");

            bootstrap.ClickAtForTesting(new Vector2(screenPoint.x, screenPoint.y));
            yield return null;

            Assert.IsTrue(bootstrap.IsBuildingPanelOpenForTesting, "Expected the building panel to open when clicking a construction site, not just a finished building.");
            Assert.AreEqual(siteId.Value, bootstrap.BuildingPanelShownIdForTesting);
        }
    }
}
