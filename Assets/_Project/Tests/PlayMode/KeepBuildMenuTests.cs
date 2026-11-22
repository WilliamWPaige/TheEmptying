using System.Collections;
using NUnit.Framework;
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
    }
}
