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
    /// 3.9d, Part 1: a hand test found that nobody ever visibly moved and a construction site's
    /// percent sat at 0 then jumped straight to 100. This drives the game for real, at normal game
    /// speed (not a manually-stepped simulation), the way a player actually experiences it, and
    /// checks both symptoms directly.
    /// </summary>
    public class RealTimePlayTests
    {
        [UnityTest]
        [Timeout(300000)] // runs for ~200 real seconds at speed 3, well past the default 180000ms UnityTest timeout
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

            var positions = new List<(double X, double Y)>();
            var progressSamples = new List<double>();

            const int sampleCount = 10;
            const float secondsBetweenSamples = 20f; // ~2 in-game hours each, at speed 3

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
    }
}
