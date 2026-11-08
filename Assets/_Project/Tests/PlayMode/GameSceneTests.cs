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

            bootstrap.AppendLogLinesForTesting(200);
            yield return null; // resolved layout needs a frame to update

            Assert.LessOrEqual(bootstrap.ConsoleLogLayout.yMax, bootstrap.ConsoleInputLayout.yMin + 0.5f,
                "The log's box must not grow past the input line's top edge, even with 200 lines in it.");
            Assert.Greater(bootstrap.ConsoleLogContentHeight, bootstrap.ConsoleLogViewportHeight,
                "200 lines should be taller than the viewport, i.e. the log should be scrollable.");

            bootstrap.ToggleConsole();
            yield return null;

            Assert.IsFalse(bootstrap.IsConsoleOpen, "Toggling again should close the console.");
        }

        [UnityTest]
        public IEnumerator Console_WhenOpened_InputFieldStartsEmpty()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            bootstrap.ToggleConsole();
            yield return null;

            Assert.IsTrue(bootstrap.IsConsoleOpen);
            Assert.AreEqual(string.Empty, bootstrap.ConsoleInputValue, "The input field should start empty, with no stray characters from the toggle key.");
        }
    }
}
