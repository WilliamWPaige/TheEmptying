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

            for (int run = 0; run < bucketCount; run++)
            {
                var processedThisRun = new List<long>();
                bucketedWork.Run(entityIds, id =>
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

            int totalRuns = bucketCount * 16;
            for (int run = 0; run < totalRuns; run++)
            {
                bucketedWork.Run(entityIds, TierFor, id => processedCounts[id]++);
            }

            Assert.AreEqual(16, processedCounts[highId]);
            Assert.AreEqual(4, processedCounts[normalId]);
            Assert.AreEqual(1, processedCounts[lowId]);
        }
    }
}
