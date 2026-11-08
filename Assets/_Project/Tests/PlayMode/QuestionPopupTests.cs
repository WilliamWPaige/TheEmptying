using System.Collections;
using NUnit.Framework;
using TheEmptying.Core.Rules;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>Prompt 2.4: the question popup, pausing, and the pause setting.</summary>
    public class QuestionPopupTests
    {
        [UnityTest]
        public IEnumerator AskingAQuestion_ShowsThePopup_AndStopsTheClock()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            bootstrap.AskTestQuestionForTesting("Pick one", "Body", new[] { "Yes", "No", "", "" }, 1);
            yield return null;

            Assert.IsTrue(bootstrap.IsQuestionPopupOpen);
            Assert.AreEqual("Pick one", bootstrap.QuestionPopupTitle);
            Assert.AreEqual(2, bootstrap.QuestionPopupChoiceCount);

            long tickWhileShown = bootstrap.CurrentTick;
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(tickWhileShown, bootstrap.CurrentTick, "The clock should not advance while a question is pending and the pause setting is on.");
        }

        [UnityTest]
        public IEnumerator ClickingAChoice_ClosesThePopup_AndResumesPlay()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            bootstrap.AskTestQuestionForTesting("Pick one", "Body", new[] { "Yes", "No", "", "" }, 1);
            yield return null;

            Assert.IsTrue(bootstrap.IsQuestionPopupOpen);

            bootstrap.ClickQuestionPopupChoiceForTesting(1);

            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.IsFalse(bootstrap.IsQuestionPopupOpen, "Clicking a choice should close the popup.");

            long tickAfterAnswering = bootstrap.CurrentTick;
            yield return new WaitForSeconds(0.3f);

            Assert.Greater(bootstrap.CurrentTick, tickAfterAnswering, "Play should resume advancing the clock once the question is answered.");
        }

        [UnityTest]
        public IEnumerator WithThePauseSettingOff_TheClockKeepsRunning_WhileThePopupShows()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            bootstrap.RunConsoleCommandForTesting($"set {RuleRunner.QuestionsPauseGameSettingId} 0");

            bootstrap.AskTestQuestionForTesting("Pick one", "Body", new[] { "Yes", "No", "", "" }, 1);
            yield return null;

            Assert.IsTrue(bootstrap.IsQuestionPopupOpen);

            long tickWhileShown = bootstrap.CurrentTick;
            yield return new WaitForSeconds(0.3f);

            Assert.Greater(bootstrap.CurrentTick, tickWhileShown, "The clock should keep running while the popup shows, since the pause setting is off.");
            Assert.IsTrue(bootstrap.IsQuestionPopupOpen, "The popup should still be showing (nothing answered it).");
        }
    }
}
