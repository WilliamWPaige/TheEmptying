using System.Collections;
using NUnit.Framework;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    public class GameSceneTests
    {
        [UnityTest]
        public IEnumerator GameScene_RunningAtNormalSpeed_AdvancesTheClockWithNoErrors()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return new WaitForSeconds(0.5f);

            Assert.Greater(bootstrap.CurrentTick, 0, "Expected the clock to have advanced at normal speed.");
        }

        [UnityTest]
        public IEnumerator Console_WhenToggledOpen_FillsMostOfTheScreenAndCloses()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            bootstrap.ToggleConsole();
            yield return null; // resolved layout needs a frame to update

            Assert.IsTrue(bootstrap.IsConsoleOpen);
            Assert.Greater(bootstrap.ConsoleRootLayout.width, Screen.width * 0.9f, "Console root should be nearly full width.");
            Assert.Greater(bootstrap.ConsoleRootLayout.height, Screen.height * 0.3f, "Console root should be at least 30% of the screen tall.");
            Assert.Greater(bootstrap.ConsoleLogLayout.height, 0f, "Output log should have a non-zero height.");
            Assert.Greater(bootstrap.ConsoleInputLayout.height, 0f, "Input line should have a non-zero height.");

            bootstrap.ToggleConsole();
            yield return null;

            Assert.IsFalse(bootstrap.IsConsoleOpen, "Toggling again should close the console.");
        }
    }
}
