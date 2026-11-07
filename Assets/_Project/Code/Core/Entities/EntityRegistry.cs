using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Entities
{
    /// <summary>
    /// Creates and destroys entity IDs. Freed indices are reused first-freed,
    /// first-reused, with a bumped generation so a stale ID is never confused
    /// with the entity now at that index.
    /// </summary>
    public sealed class EntityRegistry
    {
        private readonly List<int> _generations = new List<int>();
        private readonly List<bool> _alive = new List<bool>();
        private readonly List<EntityKind> _kinds = new List<EntityKind>();
        private readonly Queue<int> _freeIndices = new Queue<int>();
        private readonly List<IEntityTable> _tables = new List<IEntityTable>();
        private int _livingCount;

        public int Count => _livingCount;

        public EntityId Create(EntityKind kind)
        {
            int index;
            int generation;

            if (_freeIndices.Count > 0)
            {
                index = _freeIndices.Dequeue();
                generation = ++_generations[index];
            }
            else
            {
                index = _generations.Count;
                generation = 0;
                _generations.Add(generation);
                _alive.Add(false);
                _kinds.Add(kind);
            }

            _alive[index] = true;
            _kinds[index] = kind;
            _livingCount++;

            return new EntityId(index, generation);
        }

        public void Destroy(EntityId id)
        {
            EnsureAlive(id, "Destroy");

            _alive[id.Index] = false;
            _freeIndices.Enqueue(id.Index);
            _livingCount--;

            for (int i = 0; i < _tables.Count; i++)
            {
                _tables[i].Remove(id);
            }
        }

        public bool IsAlive(EntityId id)
        {
            if (id.Index < 0 || id.Index >= _generations.Count)
            {
                return false;
            }

            return _alive[id.Index] && _generations[id.Index] == id.Generation;
        }

        public EntityKind GetKind(EntityId id)
        {
            EnsureAlive(id, "GetKind");
            return _kinds[id.Index];
        }

        /// <summary>Living entities in ascending index order.</summary>
        public IEnumerable<EntityId> LivingEntities()
        {
            for (int i = 0; i < _generations.Count; i++)
            {
                if (_alive[i])
                {
                    yield return new EntityId(i, _generations[i]);
                }
            }
        }

        public void RegisterTable(IEntityTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            _tables.Add(table);
        }

        public EntityRegistryState ExportState()
        {
            return new EntityRegistryState
            {
                Generations = new List<int>(_generations),
                Alive = new List<bool>(_alive),
                Kinds = new List<EntityKind>(_kinds),
                FreeIndices = new List<int>(_freeIndices)
            };
        }

        public void RestoreState(EntityRegistryState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            _generations.Clear();
            _generations.AddRange(state.Generations);

            _alive.Clear();
            _alive.AddRange(state.Alive);

            _kinds.Clear();
            _kinds.AddRange(state.Kinds);

            _freeIndices.Clear();
            foreach (int index in state.FreeIndices)
            {
                _freeIndices.Enqueue(index);
            }

            _livingCount = 0;
            for (int i = 0; i < _alive.Count; i++)
            {
                if (_alive[i])
                {
                    _livingCount++;
                }
            }
        }

        private void EnsureAlive(EntityId id, string operation)
        {
            if (!IsAlive(id))
            {
                throw new InvalidOperationException($"{operation} called with a stale or invalid EntityId: {id}.");
            }
        }
    }
}
