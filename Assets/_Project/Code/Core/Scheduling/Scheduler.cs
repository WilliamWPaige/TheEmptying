using System;
using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Core.Scheduling
{
    /// <summary>
    /// Spreads system updates across ticks. A system runs on tick T when
    /// (T - offset) is divisible by its interval. Run order is deterministic:
    /// the order value given at registration, then Id as a tiebreaker.
    /// </summary>
    public sealed class Scheduler
    {
        private sealed class Entry
        {
            public ISimSystem System;
            public long Interval;
            public long Offset;
            public int Order;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _registeredIds = new HashSet<string>();
        private List<Entry> _sortedEntries;
        private bool _dirty;

        public void Register(ISimSystem system, long interval, long offset, int order)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            if (interval <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), "interval must be positive.");
            }

            if (!_registeredIds.Add(system.Id))
            {
                throw new InvalidOperationException($"A system with Id '{system.Id}' is already registered.");
            }

            _entries.Add(new Entry { System = system, Interval = interval, Offset = offset, Order = order });
            _dirty = true;
        }

        public void RunTick(long tick)
        {
            EnsureSorted();

            for (int i = 0; i < _sortedEntries.Count; i++)
            {
                Entry entry = _sortedEntries[i];
                if ((tick - entry.Offset) % entry.Interval == 0)
                {
                    entry.System.Update(tick);
                }
            }
        }

        private void EnsureSorted()
        {
            if (!_dirty)
            {
                return;
            }

            _sortedEntries = _entries
                .OrderBy(e => e.Order)
                .ThenBy(e => e.System.Id, StringComparer.Ordinal)
                .ToList();
            _dirty = false;
        }
    }
}
