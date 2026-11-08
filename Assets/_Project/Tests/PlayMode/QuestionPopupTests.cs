using System.Collections;
using NUnit.Framework;
using TheEmptying.Core.Rules;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>Prompt 2.4/2.5: the question popup, pausing, and the pause setting.</summary>
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
            yield return null; // the popup hides immediately; it does not wait for a tick

            Assert.IsFalse(bootstrap.IsQuestionPopupOpen, "Clicking a choice should close the popup immediately, without forcing a tick.");

            long tickAfterAnswering = bootstrap.CurrentTick;
            yield return new WaitForSeconds(0.3f);

            Assert.Greater(bootstrap.CurrentTick, tickAfterAnswering, "Play should resume advancing the clock once the question is answered.");
        }

        [UnityTest]
        public IEnumerator WithTwoPendingQuestions_ClockStaysBlockedUntilBothAreAnswered()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null;

            bootstrap.AskTestQuestionForTesting("First", "Body", new[] { "Yes", "No", "", "" }, 1);
            bootstrap.AskTestQuestionForTesting("Second", "Body", new[] { "Yes", "No", "", "" }, 1);
            yield return null;

            Assert.IsTrue(bootstrap.IsQuestionPopupOpen);
            Assert.AreEqual("First", bootstrap.QuestionPopupTitle);

            bootstrap.ClickQuestionPopupChoiceForTesting(1);
            yield return null;

            Assert.IsTrue(bootstrap.IsQuestionPopupOpen, "The second question is still unanswered, so the popup should still be showing.");
            Assert.AreEqual("Second", bootstrap.QuestionPopupTitle);

            long tickWithOneStillPending = bootstrap.CurrentTick;
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(tickWithOneStillPending, bootstrap.CurrentTick, "The clock should stay blocked while any question is still unanswered.");

            bootstrap.ClickQuestionPopupChoiceForTesting(1);
            yield return null;

            Assert.IsFalse(bootstrap.IsQuestionPopupOpen, "Both questions are answered; the popup should close.");

            long tickAfterBothAnswered = bootstrap.CurrentTick;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(bootstrap.CurrentTick, tickAfterBothAnswered, "Play should resume once every pending question has an answer.");
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
