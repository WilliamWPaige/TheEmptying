using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Scheduling
{
    /// <summary>
    /// Spreads per-entity work across a fixed number of buckets. An entity's
    /// bucket is entityId % bucketCount. Each call to Run() processes only the
    /// entities in the current bucket, then advances to the next bucket.
    /// </summary>
    public sealed class BucketedWork
    {
        private readonly int _bucketCount;
        private readonly int[] _bucketVisitCounts;
        private int _currentBucket;

        public BucketedWork(int bucketCount)
        {
            if (bucketCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bucketCount), "bucketCount must be positive.");
            }

            _bucketCount = bucketCount;
            _bucketVisitCounts = new int[bucketCount];
        }

        public int BucketCount => _bucketCount;

        public int CurrentBucket => _currentBucket;

        public int GetBucket(long entityId)
        {
            long bucket = entityId % _bucketCount;
            if (bucket < 0)
            {
                bucket += _bucketCount;
            }

            return (int)bucket;
        }

        /// <summary>Processes every entity in the current bucket, with no tier filtering.</summary>
        public void Run(IEnumerable<long> entityIds, Action<long> process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            RunInternal(entityIds, null, process);
        }

        /// <summary>
        /// Processes entities in the current bucket, gated by importance tier: an
        /// entity in a tier with multiplier m is processed only on every m-th visit
        /// to its bucket.
        /// </summary>
        public void Run(IEnumerable<long> entityIds, Func<long, ImportanceTier> tierProvider, Action<long> process)
        {
            if (tierProvider == null)
            {
                throw new ArgumentNullException(nameof(tierProvider));
            }

            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            RunInternal(entityIds, tierProvider, process);
        }

        private void RunInternal(IEnumerable<long> entityIds, Func<long, ImportanceTier> tierProvider, Action<long> process)
        {
            int bucket = _currentBucket;
            int visitIndex = _bucketVisitCounts[bucket];

            foreach (long id in entityIds)
            {
                if (GetBucket(id) != bucket)
                {
                    continue;
                }

                if (tierProvider != null)
                {
                    int multiplier = ImportanceTierConstants.GetMultiplier(tierProvider(id));
                    if (visitIndex % multiplier != 0)
                    {
                        continue;
                    }
                }

                process(id);
            }

            _bucketVisitCounts[bucket] = visitIndex + 1;
            _currentBucket = (_currentBucket + 1) % _bucketCount;
        }
    }
}
