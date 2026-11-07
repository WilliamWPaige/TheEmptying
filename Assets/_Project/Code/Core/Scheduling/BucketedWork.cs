using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Scheduling
{
    /// <summary>
    /// Spreads per-entity work across a fixed number of buckets. Stateless: the
    /// current bucket and the bucket's visit count are both derived from the run
    /// number the caller passes in, never stored. Anything stored here would need
    /// saving, and a save/load must resume exactly where a game left off.
    /// </summary>
    public sealed class BucketedWork
    {
        private readonly int _bucketCount;

        public BucketedWork(int bucketCount)
        {
            if (bucketCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bucketCount), "bucketCount must be positive.");
            }

            _bucketCount = bucketCount;
        }

        public int BucketCount => _bucketCount;

        public int GetBucket(long entityId)
        {
            long bucket = entityId % _bucketCount;
            if (bucket < 0)
            {
                bucket += _bucketCount;
            }

            return (int)bucket;
        }

        /// <summary>Processes every entity in runNumber's bucket, with no tier filtering.</summary>
        public void Run(IEnumerable<long> entityIds, long runNumber, Action<long> process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            RunInternal(entityIds, runNumber, null, process);
        }

        /// <summary>
        /// Processes entities in runNumber's bucket, gated by importance tier.
        /// An entity with multiplier m is processed when
        /// (visitIndex + entityId / bucketCount) % m == 0, which staggers entities
        /// within the same bucket across different visits instead of having them
        /// all update on the same visit.
        /// </summary>
        public void Run(IEnumerable<long> entityIds, long runNumber, Func<long, ImportanceTier> tierProvider, Action<long> process)
        {
            if (tierProvider == null)
            {
                throw new ArgumentNullException(nameof(tierProvider));
            }

            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            RunInternal(entityIds, runNumber, tierProvider, process);
        }

        private void RunInternal(IEnumerable<long> entityIds, long runNumber, Func<long, ImportanceTier> tierProvider, Action<long> process)
        {
            if (runNumber < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(runNumber), "runNumber must not be negative.");
            }

            int bucket = (int)(runNumber % _bucketCount);
            long visitIndex = runNumber / _bucketCount;

            foreach (long id in entityIds)
            {
                if (GetBucket(id) != bucket)
                {
                    continue;
                }

                if (tierProvider != null)
                {
                    long multiplier = ImportanceTierConstants.GetMultiplier(tierProvider(id));
                    if ((visitIndex + id / _bucketCount) % multiplier != 0)
                    {
                        continue;
                    }
                }

                process(id);
            }
        }
    }
}
