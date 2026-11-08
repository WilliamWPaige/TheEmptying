using System.Collections;
using System.IO;
using System.Linq;
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

        [UnityTest]
        public IEnumerator ConsoleInput_TextAndBackgroundColors_HaveClearContrast()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            bootstrap.ToggleConsole();
            yield return null;

            Color text = bootstrap.ConsoleInputTextColor;
            Color background = bootstrap.ConsoleInputBackgroundColor;
            float difference = Mathf.Abs(text.r - background.r) + Mathf.Abs(text.g - background.g) + Mathf.Abs(text.b - background.b);

            Assert.Greater(difference, 1.0f,
                $"Expected the input's text color {text} and background color {background} to contrast clearly.");
        }

        [UnityTest]
        public IEnumerator SaveAndLoad_RestoresTickEntityCountAndStateHash_ToTheMomentOfSaving()
        {
            const string saveName = "playmodetest_1_8d";
            string savePath = Path.Combine(Application.persistentDataPath, "Saves", saveName + ".sav");
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            try
            {
                bootstrap.RunConsoleCommandForTesting("spawn Person");
                bootstrap.RunConsoleCommandForTesting("skip 3 h");

                long savedTick = bootstrap.CurrentTick;
                int savedEntityCount = bootstrap.EntityCountForTesting;
                ulong savedHash = bootstrap.StateHashForTesting;

                string saveResult = bootstrap.RunConsoleCommandForTesting($"save {saveName}");
                StringAssert.DoesNotContain("failed", saveResult.ToLowerInvariant(), $"Save should have succeeded: {saveResult}");

                bootstrap.RunConsoleCommandForTesting("skip 5 h");
                Assert.AreNotEqual(savedTick, bootstrap.CurrentTick, "Sanity check: skipping further should have moved the clock past the saved moment.");

                string loadResult = bootstrap.RunConsoleCommandForTesting($"load {saveName}");
                StringAssert.DoesNotContain("failed", loadResult.ToLowerInvariant(), $"Load should have succeeded: {loadResult}");

                Assert.AreEqual(savedTick, bootstrap.CurrentTick, "The tick after loading should match the moment of saving.");
                Assert.AreEqual(savedEntityCount, bootstrap.EntityCountForTesting, "The entity count after loading should match the moment of saving.");
                Assert.AreEqual(savedHash, bootstrap.StateHashForTesting, "The state hash after loading should match the moment of saving.");
            }
            finally
            {
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }
        }

        [UnityTest]
        public IEnumerator DebugLogWarning_AppearsInTheConsoleWithTheWarningColor()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            const string marker = "1.8d test warning marker";
            LogAssert.Expect(LogType.Warning, marker);
            Debug.LogWarning(marker);
            yield return null;

            bool found = bootstrap.ConsoleLogEntries.Any(e => e.Text.Contains(marker) && e.Color == Color.yellow);
            Assert.IsTrue(found, "Expected the warning to appear in the console log, colored yellow.");
        }
    }
}
