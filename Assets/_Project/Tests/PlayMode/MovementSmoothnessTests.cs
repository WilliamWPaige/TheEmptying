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
        /// <summary>3.9f: fast-forwards the wait for in-game time to pass (see GameBootstrap.SetTestTimeMultiplierForTesting) - nobody is watching this run in real time, only the sampled positions matter.</summary>
        private const double TestFastForwardMultiplier = 10.0;

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
            // No one is watching this run smoothly - only the sampled positions below matter - so
            // fast-forward the wait for hour 7 well past what real wall-clock speed-3 would take.
            // See GameBootstrap.SetTestTimeMultiplierForTesting.
            bootstrap.SetTestTimeMultiplierForTesting(TestFastForwardMultiplier);

            // 3.9h: the game now starts at noon (hour 12), already inside the day shift
            // (setting.jobs.day_shift_start_hour/end_hour, default 7-15) - a founder should start
            // commuting to the waiting site almost immediately once unpaused, not after waiting for a
            // shift start that's already in the past. Poll finely from the start instead of the old
            // long coarse wait (which, at this fast-forward rate, would now run straight through the
            // rest of the shift and into night before ever checking). Batch mode renders at a very
            // high, uneven frame rate, so polling by frame count doesn't reliably map to real time.
            // At speed 3 with this fast-forward multiplier, the game runs 600 ticks per real second,
            // so the first hourly tick (where ChooseActivity actually sets Commuting) lands about one
            // real second after unpausing - this deadline is a flat real-second budget, not divided
            // by the multiplier, since it's the actual wall-clock time to allow, independent of how
            // much game-time races by within it.
            bool isCommuting = false;
            string lastPersonInfo = "";
            float fineDeadline = Time.realtimeSinceStartup + 10f;
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
