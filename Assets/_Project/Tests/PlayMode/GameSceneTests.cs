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
    }
}
