using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Scheduling;

namespace TheEmptying.Tests.EditMode.Scheduling
{
    public class BucketedWorkTests
    {
        [Test]
        public void Run_OverFullCycle_ProcessesEveryEntityExactlyOnce()
        {
            const int entityCount = 1000;
            const int bucketCount = 10;

            var entityIds = new List<long>();
            for (long i = 0; i < entityCount; i++)
            {
                entityIds.Add(i);
            }

            var bucketedWork = new BucketedWork(bucketCount);
            var processedCounts = new Dictionary<long, int>();
            foreach (long id in entityIds)
            {
                processedCounts[id] = 0;
            }

            for (long run = 0; run < bucketCount; run++)
            {
                var processedThisRun = new List<long>();
                bucketedWork.Run(entityIds, run, id =>
                {
                    processedThisRun.Add(id);
                    processedCounts[id]++;
                });

                Assert.IsTrue(processedThisRun.Count > 0, "Each run should process at least one entity.");
                int expectedBucket = bucketedWork.GetBucket(processedThisRun[0]);
                Assert.IsTrue(processedThisRun.TrueForAll(id => bucketedWork.GetBucket(id) == expectedBucket));
            }

            Assert.IsTrue(processedCounts.Values.All(count => count == 1));
        }

        [Test]
        public void Run_SameRunNumberCalledTwice_GivesSameResult()
        {
            const int bucketCount = 10;
            var entityIds = new List<long>();
            for (long i = 0; i < 50; i++)
            {
                entityIds.Add(i);
            }

            var bucketedWork = new BucketedWork(bucketCount);

            var firstCall = new List<long>();
            bucketedWork.Run(entityIds, 23, id => firstCall.Add(id));

            var secondCall = new List<long>();
            bucketedWork.Run(entityIds, 23, id => secondCall.Add(id));

            CollectionAssert.AreEqual(firstCall, secondCall);
        }

        [Test]
        public void Tiers_OverSixteenFullCycles_HighNormalLowCountsMatchMultipliers()
        {
            const int bucketCount = 10;
            const long highId = 0;
            const long normalId = 1;
            const long lowId = 2;

            var entityIds = new List<long> { highId, normalId, lowId };
            var bucketedWork = new BucketedWork(bucketCount);
            var processedCounts = new Dictionary<long, int> { { highId, 0 }, { normalId, 0 }, { lowId, 0 } };

            ImportanceTier TierFor(long id)
            {
                if (id == highId)
                {
                    return ImportanceTier.High;
                }

                if (id == lowId)
                {
                    return ImportanceTier.Low;
                }

                return ImportanceTier.Normal;
            }

            long totalRuns = bucketCount * 16;
            for (long run = 0; run < totalRuns; run++)
            {
                bucketedWork.Run(entityIds, run, TierFor, id => processedCounts[id]++);
            }

            Assert.AreEqual(16, processedCounts[highId]);
            Assert.AreEqual(4, processedCounts[normalId]);
            Assert.AreEqual(1, processedCounts[lowId]);
        }

        [Test]
        public void Tiers_NormalEntities_ProcessedCountPerRun_HasNoSpikes()
        {
            const int entityCount = 1000;
            const int bucketCount = 10;

            var entityIds = new List<long>();
            for (long i = 0; i < entityCount; i++)
            {
                entityIds.Add(i);
            }

            var bucketedWork = new BucketedWork(bucketCount);
            var countsPerRun = new List<int>();

            long totalRuns = bucketCount * ImportanceTierConstants.NormalMultiplier;
            for (long run = 0; run < totalRuns; run++)
            {
                int processed = 0;
                bucketedWork.Run(entityIds, run, _ => ImportanceTier.Normal, _ => processed++);
                countsPerRun.Add(processed);
            }

            double expected = (double)entityCount / bucketCount / ImportanceTierConstants.NormalMultiplier;

            Assert.LessOrEqual(countsPerRun.Max() - countsPerRun.Min(), 2, "Per-run processed counts should stay flat, not spike.");
            Assert.That(countsPerRun.Average(), Is.EqualTo(expected).Within(1.0));
        }
    }
}
