using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.PlayMode
{
    /// <summary>
    /// 3.9d, Part 1: a hand test found that nobody ever visibly moved and a construction site's
    /// percent sat at 0 then jumped straight to 100. This drives the game for real, at normal game
    /// speed (not a manually-stepped simulation), the way a player actually experiences it, and
    /// checks both symptoms directly.
    /// </summary>
    public class RealTimePlayTests
    {
        /// <summary>3.9f: fast-forwards the wait for in-game time to pass (see GameBootstrap.SetTestTimeMultiplierForTesting) - nobody is watching these run in real time, only the sampled state matters.</summary>
        private const double TestFastForwardMultiplier = 10.0;

        [UnityTest]
        [Timeout(300000)] // well past the default 180000ms UnityTest timeout, even with the fast-forward
        public IEnumerator FoundersMove_AndConstructionProgressRisesGradually_OverSeveralRealTimeDays()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            // The player's first action: this alone should already set founders to work (even before
            // any other building exists) - see Part 1's root cause, the missing immediate task
            // assignment on stockpile placement.
            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            EntityId? siteId = bootstrap.SimulatePlacementClickForTesting("building.crown_lodging", new GridPoint(115, 115), new Vector2(Screen.width - 5f, 5f));
            Assert.IsTrue(siteId.HasValue, "Setup: placing the crown lodging site should succeed.");
            bootstrap.CloseBuildMenuForTesting();
            yield return null;

            string peopleList = bootstrap.RunConsoleCommandForTesting("people");
            string firstLine = peopleList.Split('\n')[0];
            Assert.IsTrue(EntityId.TryParse(firstLine.Split(':')[0].Trim(), out EntityId founder), $"Expected to parse a founder id from: {firstLine}");

            bootstrap.SetSpeed(3);
            bootstrap.SetPaused(false);
            bootstrap.SetTestTimeMultiplierForTesting(TestFastForwardMultiplier);

            var positions = new List<(double X, double Y)>();
            var progressSamples = new List<double>();

            const int sampleCount = 10;
            const float secondsBetweenSamples = 20f / (float)TestFastForwardMultiplier; // ~2 in-game hours each, at speed 3

            for (int i = 0; i < sampleCount; i++)
            {
                yield return new WaitForSecondsRealtime(secondsBetweenSamples);

                (double X, double Y)? position = bootstrap.MoverPositionForTesting(founder);
                Assert.IsTrue(position.HasValue, $"Expected founder {founder} to still have a tracked position at sample {i}.");
                positions.Add(position.Value);

                progressSamples.Add(bootstrap.ConstructionProgressPercentForTesting(siteId.Value));
            }

            bool everMoved = false;
            for (int i = 1; i < positions.Count; i++)
            {
                if (positions[i] != positions[0])
                {
                    everMoved = true;
                    break;
                }
            }

            Assert.IsTrue(everMoved, "Expected the founder's position to change at least once over several real-time in-game days.");

            // "Gradual" means at least one sample caught construction meaningfully in between -
            // not every sample reading either ~0% (not started) or 100% (already done).
            bool sawMidProgress = progressSamples.Exists(p => p > 5.0 && p < 95.0);
            Assert.IsTrue(sawMidProgress,
                $"Expected construction progress to pass through a mid-range value, not jump straight from ~0% to 100%. Samples: [{string.Join(", ", progressSamples)}]");
        }

        /// <summary>
        /// 3.9f, Part 3: a hand test found founders stopped doing anything once a building finished.
        /// The cause was GatheringModule never reassigning tasks when a site completes (only at the
        /// next morning's NewDayEvent), so everyone who was building sat idle (GatherTaskKind.None)
        /// until the next in-game day. This drives a real game through an actual completion and a
        /// full extra day, checking founders keep moving and stay tasked the whole time.
        /// </summary>
        [UnityTest]
        [Timeout(600000)] // construction completion + a full extra in-game day, at speed 3
        public IEnumerator FoundersReassign_AssoonAsConstructionCompletes_AndStayBusyThroughTheNextDay()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            // The smallest government building (20 planks + 10 stone, well covered by the initial
            // stockpile's seed resources), so it finishes quickly with several founders building it.
            EntityId? siteId = bootstrap.SimulatePlacementClickForTesting("building.crown_storehouse", new GridPoint(110, 110), new Vector2(Screen.width - 5f, 5f));
            Assert.IsTrue(siteId.HasValue, "Setup: placing the crown storehouse site should succeed.");
            bootstrap.CloseBuildMenuForTesting();
            yield return null;

            // "people" lists everyone, including the monarch, who never has a gathering task - only
            // founders are relevant to this test.
            string peopleList = bootstrap.RunConsoleCommandForTesting("people");
            var founders = new List<EntityId>();
            foreach (string line in peopleList.Split('\n'))
            {
                if (EntityId.TryParse(line.Split(':')[0].Trim(), out EntityId id) && bootstrap.IsFounderForTesting(id))
                {
                    founders.Add(id);
                }
            }
            Assert.Greater(founders.Count, 0, $"Expected to parse at least one founder id from: {peopleList}");

            bootstrap.SetSpeed(3);
            bootstrap.SetPaused(false);
            bootstrap.SetTestTimeMultiplierForTesting(TestFastForwardMultiplier);

            double progress = 0.0;
            float deadline = Time.realtimeSinceStartup + 240f / (float)TestFastForwardMultiplier;
            while (progress < 100.0 && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(1f);
                progress = bootstrap.ConstructionProgressPercentForTesting(siteId.Value);
            }

            Assert.AreEqual(100.0, progress, 0.01,
                $"Expected the storehouse to finish construction within a reasonable time. tick={bootstrap.CurrentTick}, last progress={progress}");

            long tickAtCompletion = bootstrap.CurrentTick;
            long targetTick = tickAtCompletion + CalendarConstants.TicksPerDay;

            // Founders mostly walk only in brief bursts (arriving at a task, going home at shift
            // end) and sit still working the rest of the time - at the real walking speed (see
            // MovementSmoothnessTests and this prompt's report), a single walk can finish in well
            // under a second. A single before/after snapshot can land entirely within a stationary
            // stretch and miss every walk, so sample every poll across the whole extra day instead
            // of only at the end, and treat any position change between consecutive polls as a hit.
            var lastPositions = new Dictionary<EntityId, (double X, double Y)>();
            foreach (EntityId id in founders)
            {
                (double X, double Y)? startPos = bootstrap.MoverPositionForTesting(id);
                if (startPos.HasValue)
                {
                    lastPositions[id] = startPos.Value;
                }
            }

            bool anyMoved = false;
            deadline = Time.realtimeSinceStartup + 300f / (float)TestFastForwardMultiplier;
            while (bootstrap.CurrentTick < targetTick && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(1f);

                foreach (EntityId id in founders)
                {
                    (double X, double Y)? pos = bootstrap.MoverPositionForTesting(id);
                    if (!pos.HasValue)
                    {
                        continue;
                    }

                    if (lastPositions.TryGetValue(id, out (double X, double Y) last) && (pos.Value.X != last.X || pos.Value.Y != last.Y))
                    {
                        anyMoved = true;
                    }

                    lastPositions[id] = pos.Value;
                }
            }

            Assert.GreaterOrEqual(bootstrap.CurrentTick, targetTick,
                $"Expected a full extra in-game day to pass. tickAtCompletion={tickAtCompletion}, targetTick={targetTick}, currentTick={bootstrap.CurrentTick}");

            Assert.IsTrue(anyMoved, "Expected at least one founder to change position at some point during the extra day.");

            // tickAtCompletion necessarily falls during the day shift (labor is only applied then),
            // and targetTick is exactly one day later at the same hour - so this is still shift time.
            // A founder hired into the storehouse's own new job slot correctly has no gathering task
            // (they have a real job instead - see GatheringModule.DescribeIdleReason), not an idle one.
            foreach (EntityId id in founders)
            {
                if (bootstrap.IsEmployedForTesting(id))
                {
                    continue;
                }

                GatherTaskKind kind = bootstrap.GatheringTaskKindForTesting(id);
                Assert.AreNotEqual(GatherTaskKind.None, kind,
                    $"Expected founder {id} to have a gathering task during work hours, not sit idle (tick={bootstrap.CurrentTick}).");
            }
        }

        /// <summary>
        /// 3.10b3: the user's hand test - a true new game, the stockpile and a crown lodging placed
        /// through the real build menu path, driven at real game speed. Confirms the actual fix for
        /// the 7% stall: progress passes 7% within the first in-game day (not stuck there forever),
        /// never stalls longer than about a haul trip's worth of time, and the lodging finishes - and
        /// the building panel actually lists the founders building and hauling for it (not "no
        /// builders" while every founder's own label says otherwise).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator CrownLodging_HaulsMaterialsAndBuilds_NeverStallsForLong_AndFinishes_WithWorkersShownInThePanel()
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Single);

            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(bootstrap, "Expected a GameBootstrap in the Game scene.");

            yield return null; // let the initial layout settle

            Assert.IsTrue(bootstrap.PlaceInitialStockpileForTesting(new GridPoint(100, 100)), "Setup: placing the stockpile should succeed.");
            yield return null;

            EntityId? siteId = bootstrap.SimulatePlacementClickForTesting("building.crown_lodging", new GridPoint(115, 115), new Vector2(Screen.width - 5f, 5f));
            Assert.IsTrue(siteId.HasValue, "Setup: placing the crown lodging site should succeed.");
            bootstrap.CloseBuildMenuForTesting();

            yield return null; // let the site's cube actually appear this frame
            yield return null;

            bootstrap.SetSpeed(3);
            bootstrap.SetPaused(false);
            bootstrap.SetTestTimeMultiplierForTesting(TestFastForwardMultiplier);

            double cellSize = bootstrap.CellSizeMetersForTesting;
            var worldCenter = new Vector3((float)((115 + 4 * 0.5) * cellSize), 1f, (float)((115 + 4 * 0.5) * cellSize));
            Camera camera = bootstrap.CameraForTesting;
            Vector3 screenPoint = camera.WorldToScreenPoint(worldCenter);
            bootstrap.ClickAtForTesting(new Vector2(screenPoint.x, screenPoint.y));
            yield return null;

            Assert.IsTrue(bootstrap.IsBuildingPanelOpenForTesting, "Setup: expected the building panel to actually open on the crown lodging.");

            long startTick = bootstrap.CurrentTick;
            long passedSevenPercentAtTick = -1;
            double lastProgress = 0.0;
            long lastProgressChangeTick = startTick;
            long longestStallTicks = 0;
            double progress = 0.0;
            bool sawWorkersInPanel = false;

            // A crown lodging needs roughly 1.5-2 in-game days of hauling and building to finish;
            // at 10x fast-forward that's well under a minute and a half of real time, but give it
            // generous headroom rather than cutting it close.
            float deadline = Time.realtimeSinceStartup + 1500f / (float)TestFastForwardMultiplier;
            while (progress < 100.0 && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(1f);
                progress = bootstrap.ConstructionProgressPercentForTesting(siteId.Value);
                long tick = bootstrap.CurrentTick;

                if (passedSevenPercentAtTick < 0 && progress > 7.0)
                {
                    passedSevenPercentAtTick = tick;
                }

                if (progress > lastProgress + 0.01)
                {
                    longestStallTicks = System.Math.Max(longestStallTicks, tick - lastProgressChangeTick);
                    lastProgress = progress;
                    lastProgressChangeTick = tick;
                }

                if (!sawWorkersInPanel && bootstrap.BuildingPanelSlotLabelTextsForTesting.Any(l => l.Contains("building") || l.Contains("hauling")))
                {
                    sawWorkersInPanel = true;
                }
            }

            Assert.AreEqual(100.0, progress, 0.01,
                $"Expected the crown lodging to finish, not stall. Last progress={progress}, tick={bootstrap.CurrentTick}.");

            Assert.IsTrue(passedSevenPercentAtTick >= 0 && passedSevenPercentAtTick - startTick <= CalendarConstants.TicksPerDay * 2,
                $"Expected progress to pass 7% within about the first in-game day or two, not stay stuck there - passed at tick {passedSevenPercentAtTick}, started at {startTick}.");

            Assert.Less(longestStallTicks, CalendarConstants.TicksPerDay * 3,
                $"Expected no stall longer than about a haul trip (plus a night's rest) would take, not {longestStallTicks} ticks.");

            Assert.IsTrue(sawWorkersInPanel,
                $"Expected the building panel to list at least one founder building or hauling for the lodging while it was still under construction. Last seen labels: [{string.Join(" | ", bootstrap.BuildingPanelSlotLabelTextsForTesting)}]");
        }
    }
}
