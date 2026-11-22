using System.Collections;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>
    /// 3.10b2: a hand test found no way to see what the crown stockpile actually held. A HUD button
    /// opens a dedicated panel listing its contents, and clicking the stockpile building itself shows
    /// the same list in the building panel.
    /// </summary>
    public class StockpilePanelTests
    {
        [UnityTest]
        public IEnumerator StockpilePanel_OpensFromTheHudButton_AndListsTheStartingGoods()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            Assert.IsFalse(bootstrap.IsStockpilePanelOpenForTesting, "Setup: the panel should start closed.");

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            bootstrap.ShowStockpilePanelForTesting();
            yield return null;

            Assert.IsTrue(bootstrap.IsStockpilePanelOpenForTesting, "Expected the stockpile panel to open.");
            Assert.Greater(bootstrap.StockpilePanelGoodRowCountForTesting, 0, "Expected the true default stockpile (planks, stone and provisions) to list at least one good.");
        }

        [UnityTest]
        public IEnumerator ClickingTheStockpileBuilding_ShowsItsContentsInTheBuildingPanel()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;
            yield return null; // let the stockpile's cube actually appear this frame

            double cellSize = bootstrap.CellSizeMetersForTesting;
            var worldCenter = new Vector3((float)((100 + 4 * 0.5) * cellSize), 1f, (float)((100 + 4 * 0.5) * cellSize)); // the crown storehouse's 4x4 footprint, centered

            Camera camera = bootstrap.CameraForTesting;
            Assert.IsNotNull(camera, "Expected a main camera.");
            Vector3 screenPoint = camera.WorldToScreenPoint(worldCenter);
            Assert.Greater(screenPoint.z, 0f, "Sanity check: the stockpile should be in front of the camera.");

            bootstrap.ClickAtForTesting(new Vector2(screenPoint.x, screenPoint.y));
            yield return null;

            Assert.IsTrue(bootstrap.IsBuildingPanelOpenForTesting, "Expected clicking the stockpile to open the building panel.");
            Assert.Greater(bootstrap.BuildingPanelSlotsChildCountForTesting, 1, "Expected the building panel to list the stockpile's contents (a header plus at least one good), not just job slots.");
        }
    }
}
