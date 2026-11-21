using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>
    /// 3.9f, Part 2: every hand-gathering task change should log one line (who, old task, new
    /// task, why) behind setting.debug.log_task_changes - on by default in the Editor (see
    /// GameBootstrap.ForceDebugLoggingDefaultInEditor and SubscribeDebugEventLogging).
    /// </summary>
    public class TaskChangeLoggingTests
    {
        [UnityTest]
        public IEnumerator PlacingTheStockpile_LogsATaskChange_ForTheFoundersItAssignsImmediately()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            // Batch-mode test runs don't get the Editor's forced-on debug logging default (see
            // ForceDebugLoggingDefaultInEditor - forcing it on in batch mode would flood every long
            // real-time test with log lines), so this test turns it on itself.
            bootstrap.RunConsoleCommandForTesting("set setting.debug.log_task_changes 1");

            // GatheringModule.AssignTasks runs immediately on InitialStockpilePlacedEventId, moving
            // every eligible founder from "no task" to farming or gathering - each one is a logged
            // task change.
            LogAssert.Expect(LogType.Log, new Regex(@"^\[TaskChange\] "));

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");

            // InitialStockpilePlacedEventId delivers at the end of a real simulation tick, not the
            // instant this call returns - a frame-count wait isn't a reliable proxy for that (batch
            // mode can render several frames before even one tick's worth of real time has
            // accumulated at normal speed), so wait a short real interval instead.
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}
