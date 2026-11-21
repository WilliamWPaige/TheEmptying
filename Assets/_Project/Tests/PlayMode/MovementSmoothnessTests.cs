using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>
    /// 3.9e, Fix 1: a hand test found people appearing to teleport once a minute instead of gliding.
    /// The cause was the presenter interpolating only the last simulation tick of however many ran in
    /// a single real frame, discarding everything earlier in that batch - this drives the game for
    /// real, at the fastest game speed (most likely to batch several ticks per frame), and samples the
    /// actual drawn position every single frame to check the steps are steady, not a mix of near-zero
    /// steps and one big jump.
    /// </summary>
    public class MovementSmoothnessTests
    {
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator MovingFounder_DrawnPosition_AdvancesSteadily_NeverInASuddenJump()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(10, 10)), "Setup: placing the stockpile should succeed.");
            yield return null;

            // Far from the stockpile, so founders walking to build it have a long enough walk to
            // sample many frames of genuine, ongoing movement.
            EntityId? siteId = bootstrap.SimulatePlacementClickForTesting("building.crown_lodging", new GridPoint(240, 240), new Vector2(Screen.width - 5f, 5f));
            Assert.IsTrue(siteId.HasValue, "Setup: placing the crown lodging site should succeed.");
            bootstrap.CloseBuildMenuForTesting();
            yield return null;

            string peopleList = bootstrap.RunConsoleCommandForTesting("people");
            string firstLine = peopleList.Split('\n')[0];
            Assert.IsTrue(EntityId.TryParse(firstLine.Split(':')[0].Trim(), out EntityId founder), $"Expected to parse a founder id from: {firstLine}");

            bootstrap.SetSpeed(3);
            bootstrap.SetPaused(false);

            // The game starts at hour 0, and the day shift (hence any founder actually walking
            // anywhere) doesn't begin until setting.jobs.day_shift_start_hour (default 7, i.e. 4200
            // ticks - about 70 real seconds at speed 3). Batch mode renders at a very high, uneven
            // frame rate, so polling by frame count doesn't reliably map to real time, and calling a
            // parsed console command every single frame is expensive enough to throttle that frame
            // rate itself - wait the bulk of it coarsely (real time, not frames), then poll finely
            // only once close, so the brief commuting window itself isn't missed.
            yield return new WaitForSecondsRealtime(60f);

            bool isCommuting = false;
            string lastPersonInfo = "";
            float fineDeadline = Time.realtimeSinceStartup + 40f;
            while (!isCommuting && Time.realtimeSinceStartup < fineDeadline)
            {
                yield return new WaitForSecondsRealtime(0.1f);
                lastPersonInfo = bootstrap.RunConsoleCommandForTesting($"person {founder}");
                isCommuting = lastPersonInfo.Contains("activity: Commuting");
            }

            Assert.IsTrue(isCommuting, $"Expected the founder to start commuting to the construction site within a reasonable wait. Last seen: [{lastPersonInfo.Replace("\n", " ; ")}] tick={bootstrap.CurrentTick}");

            var positions = new List<Vector3>();
            const int sampleFrames = 180; // one sample per rendered frame, starting right as the walk begins

            for (int i = 0; i < sampleFrames; i++)
            {
                yield return null;
                Vector3? position = bootstrap.MoverRenderedPositionForTesting(founder);
                if (position.HasValue)
                {
                    positions.Add(position.Value);
                }
            }

            Assert.Greater(positions.Count, sampleFrames / 2, "Expected the founder to have a drawn position for most sampled frames.");

            float totalDistance = 0f;
            float maxStep = 0f;
            for (int i = 1; i < positions.Count; i++)
            {
                float step = Vector3.Distance(positions[i - 1], positions[i]);
                totalDistance += step;
                if (step > maxStep)
                {
                    maxStep = step;
                }
            }

            Assert.Greater(totalDistance, 1f, "Expected the founder to have genuinely walked a meaningful distance over the sampled window.");

            // "Steady, not a sudden jump" means no single frame's step dominates the whole walk - a
            // smoothly-interpolated glide spreads the distance over many frames, while a teleport
            // would show up as one enormous step among many near-zero ones.
            Assert.Less(maxStep, totalDistance * 0.25f,
                $"Expected no single frame to account for more than a quarter of the total distance walked. maxStep={maxStep}, totalDistance={totalDistance}");
        }
    }
}
