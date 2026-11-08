using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Memory
{
    /// <summary>
    /// Named true-or-false flags and named numeric counters, each owned by an
    /// entity or by the kingdom (EntityId.None). Registers with the entity
    /// registry like a table, so destroying an entity removes its rows.
    /// </summary>
    public sealed class MemoryStore : IEntityTable
    {
        private readonly Dictionary<(EntityId Owner, string Name), bool> _flags = new Dictionary<(EntityId, string), bool>();
        private readonly Dictionary<(EntityId Owner, string Name), double> _counters = new Dictionary<(EntityId, string), double>();

        public MemoryStore(EntityRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.RegisterTable(this);
        }

        public bool GetFlag(EntityId owner, string name) => _flags.TryGetValue((owner, name), out bool value) && value;

        public void SetFlag(EntityId owner, string name, bool value) => _flags[(owner, name)] = value;

        public double GetCounter(EntityId owner, string name) => _counters.TryGetValue((owner, name), out double value) ? value : 0.0;

        public void SetCounter(EntityId owner, string name, double value) => _counters[(owner, name)] = value;

        public double ChangeCounter(EntityId owner, string name, double amount)
        {
            double updated = GetCounter(owner, name) + amount;
            _counters[(owner, name)] = updated;
            return updated;
        }

        public bool Has(EntityId id)
        {
            return _flags.Keys.Any(k => k.Owner.Equals(id)) || _counters.Keys.Any(k => k.Owner.Equals(id));
        }

        public void Remove(EntityId id)
        {
            RemoveWhereOwner(_flags, id);
            RemoveWhereOwner(_counters, id);
        }

        /// <summary>All flags, ordered by owner index then name (ordinal).</summary>
        public IEnumerable<(EntityId Owner, string Name, bool Value)> AllFlags()
        {
            return _flags
                .OrderBy(kvp => kvp.Key.Owner.Index)
                .ThenBy(kvp => kvp.Key.Name, StringComparer.Ordinal)
                .Select(kvp => (kvp.Key.Owner, kvp.Key.Name, kvp.Value));
        }

        /// <summary>All counters, ordered by owner index then name (ordinal).</summary>
        public IEnumerable<(EntityId Owner, string Name, double Value)> AllCounters()
        {
            return _counters
                .OrderBy(kvp => kvp.Key.Owner.Index)
                .ThenBy(kvp => kvp.Key.Name, StringComparer.Ordinal)
                .Select(kvp => (kvp.Key.Owner, kvp.Key.Name, kvp.Value));
        }

        /// <summary>One owner's flags and counters, name-ordered, as readable lines.</summary>
        public IEnumerable<string> Describe(EntityId owner)
        {
            foreach ((EntityId o, string name, bool value) in AllFlags())
            {
                if (o.Equals(owner))
                {
                    yield return $"flag.{name} = {(value ? "true" : "false")}";
                }
            }

            foreach ((EntityId o, string name, double value) in AllCounters())
            {
                if (o.Equals(owner))
                {
                    yield return $"counter.{name} = {value}";
                }
            }
        }

        private static void RemoveWhereOwner<T>(Dictionary<(EntityId Owner, string Name), T> dictionary, EntityId owner)
        {
            List<(EntityId, string)> toRemove = null;
            foreach ((EntityId, string) key in dictionary.Keys)
            {
                if (key.Item1.Equals(owner))
                {
                    (toRemove ??= new List<(EntityId, string)>()).Add(key);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            foreach ((EntityId, string) key in toRemove)
            {
                dictionary.Remove(key);
            }
        }
    }
}
