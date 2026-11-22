using System.Collections;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>3.9i: the keep already exists from the start of the game, so it's never offered in the build menu.</summary>
    public class KeepBuildMenuTests
    {
        [UnityTest]
        public IEnumerator BuildMenu_NeverOffersTheKeep()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            // Open the menu with a known, ordinary government type selected - confirms the menu is
            // genuinely open, so a null result for the keep below means "excluded", not "menu closed".
            bootstrap.OpenBuildMenuWithTypeSelectedForTesting("building.crown_lodging");
            yield return null;

            Assert.IsTrue(bootstrap.IsBuildMenuOpenForTesting, "Setup: expected the build menu to be open.");
            Assert.IsTrue(bootstrap.MenuTypeButtonPositionForTesting("building.crown_lodging").HasValue, "Setup: expected an ordinary government building to still have a menu button.");

            Assert.IsFalse(bootstrap.MenuTypeButtonPositionForTesting("building.keep").HasValue, "Expected no build-menu button for the keep.");
        }

        /// <summary>3.10b: "the people decide the work, not the player" - a founder-built starter building never appears in the player's build menu, even though it's still a government building underneath.</summary>
        [UnityTest]
        public IEnumerator BuildMenu_OnlyOffersGovernmentBuildings_NeverTheFoundersOwnStarterBuildings()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            bootstrap.OpenBuildMenuWithTypeSelectedForTesting("building.crown_lodging");
            yield return null;

            Assert.IsTrue(bootstrap.IsBuildMenuOpenForTesting, "Setup: expected the build menu to be open.");
            Assert.IsTrue(bootstrap.MenuTypeButtonPositionForTesting("building.crown_lodging").HasValue, "Setup: expected an ordinary government building to still have a menu button.");

            foreach (string starterTypeId in new[] { "building.saw_pit", "building.log_cabin", "building.woodcutters_camp", "building.quarry", "building.stone_yard" })
            {
                Assert.IsFalse(bootstrap.MenuTypeButtonPositionForTesting(starterTypeId).HasValue, $"Expected no build-menu button for '{starterTypeId}' - the founders decide to build it themselves.");
            }
        }

        /// <summary>3.10b2: a hand test found the build menu gave no idea what a building would cost. Each entry shows every material's amount needed and currently in the stockpile, in a warning color when short.</summary>
        [UnityTest]
        public IEnumerator BuildMenu_ShowsEachEntrysCost_WithAmountNeededAndInStock()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            // No stockpile yet at all - every material should read "have 0", in a warning color.
            bootstrap.OpenBuildMenuWithTypeSelectedForTesting("building.crown_lodging");
            yield return null;

            string costBeforeStockpile = bootstrap.MenuTypeCostTextForTesting("building.crown_lodging");
            Assert.IsNotNull(costBeforeStockpile, "Expected a cost label for the crown lodging.");
            Assert.That(costBeforeStockpile, Does.Contain("60"), "Expected the plank amount needed (60) to be shown.");
            Assert.That(costBeforeStockpile, Does.Contain("20"), "Expected the stone amount needed (20) to be shown.");
            Assert.That(costBeforeStockpile, Does.Contain("have 0"), "Expected 'have 0' with no stockpile placed yet.");
            Assert.That(costBeforeStockpile, Does.Contain("<color="), "Expected a shortfall to be shown in a warning color.");

            bootstrap.CloseBuildMenuForTesting();

            // The true default stockpile (100 planks, 40 stone) covers the lodging's cost (60, 20) on its own.
            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            bootstrap.OpenBuildMenuWithTypeSelectedForTesting("building.crown_lodging");
            yield return null;

            string costAfterStockpile = bootstrap.MenuTypeCostTextForTesting("building.crown_lodging");
            Assert.That(costAfterStockpile, Does.Contain("have 100"), "Expected 'have 100' once the default stockpile is placed.");
            Assert.That(costAfterStockpile, Does.Contain("have 40"), "Expected 'have 40' once the default stockpile is placed.");
            Assert.AreNotEqual(costBeforeStockpile, costAfterStockpile, "Expected the live 'have' amounts to update once the stockpile exists.");
        }
    }
}
