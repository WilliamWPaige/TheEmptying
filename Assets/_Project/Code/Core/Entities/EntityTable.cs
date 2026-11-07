using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Entities
{
    /// <summary>
    /// Module data of type T keyed by EntityId. Dense array storage with an index
    /// lookup: fast add/remove/get, with iteration sorted into ascending entity
    /// index order on demand (removal uses swap-with-last, so storage order itself
    /// is not sorted).
    /// </summary>
    public sealed class EntityTable<T> : IEntityTable
    {
        private readonly EntityRegistry _registry;
        private readonly Dictionary<int, int> _slotByIndex = new Dictionary<int, int>();

        private int[] _entityIndices = new int[4];
        private int[] _generations = new int[4];
        private T[] _values = new T[4];
        private int _count;

        public EntityTable(EntityRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _registry.RegisterTable(this);
        }

        public int Count => _count;

        public void Add(EntityId id, T value)
        {
            if (!_registry.IsAlive(id))
            {
                throw new InvalidOperationException($"Cannot add a row for a dead or stale EntityId: {id}.");
            }

            if (_slotByIndex.ContainsKey(id.Index))
            {
                throw new InvalidOperationException($"This table already has a row for {id}.");
            }

            EnsureCapacity(_count + 1);

            int slot = _count;
            _entityIndices[slot] = id.Index;
            _generations[slot] = id.Generation;
            _values[slot] = value;
            _slotByIndex[id.Index] = slot;
            _count++;
        }

        public bool Has(EntityId id)
        {
            return _slotByIndex.TryGetValue(id.Index, out int slot) && _generations[slot] == id.Generation;
        }

        public bool TryGet(EntityId id, out T value)
        {
            if (_slotByIndex.TryGetValue(id.Index, out int slot) && _generations[slot] == id.Generation)
            {
                value = _values[slot];
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>Reference to the stored value, so structs can be edited in place.</summary>
        public ref T Get(EntityId id)
        {
            if (!_slotByIndex.TryGetValue(id.Index, out int slot) || _generations[slot] != id.Generation)
            {
                throw new KeyNotFoundException($"This table has no row for {id}.");
            }

            return ref _values[slot];
        }

        public void Set(EntityId id, T value)
        {
            if (!_slotByIndex.TryGetValue(id.Index, out int slot) || _generations[slot] != id.Generation)
            {
                throw new KeyNotFoundException($"This table has no row for {id}. Use Add for a new row.");
            }

            _values[slot] = value;
        }

        public void Remove(EntityId id)
        {
            if (!_slotByIndex.TryGetValue(id.Index, out int slot))
            {
                return;
            }

            int lastSlot = _count - 1;
            if (slot != lastSlot)
            {
                _entityIndices[slot] = _entityIndices[lastSlot];
                _generations[slot] = _generations[lastSlot];
                _values[slot] = _values[lastSlot];
                _slotByIndex[_entityIndices[slot]] = slot;
            }

            _entityIndices[lastSlot] = default;
            _generations[lastSlot] = default;
            _values[lastSlot] = default;
            _count--;

            _slotByIndex.Remove(id.Index);
        }

        /// <summary>Rows in ascending entity index order, regardless of add/remove history.</summary>
        public IEnumerable<KeyValuePair<EntityId, T>> Rows()
        {
            var slots = new int[_count];
            for (int i = 0; i < _count; i++)
            {
                slots[i] = i;
            }

            Array.Sort(slots, (a, b) => _entityIndices[a].CompareTo(_entityIndices[b]));

            foreach (int slot in slots)
            {
                yield return new KeyValuePair<EntityId, T>(new EntityId(_entityIndices[slot], _generations[slot]), _values[slot]);
            }
        }

        private void EnsureCapacity(int minimum)
        {
            if (_values.Length >= minimum)
            {
                return;
            }

            int newCapacity = Math.Max(_values.Length * 2, minimum);
            Array.Resize(ref _entityIndices, newCapacity);
            Array.Resize(ref _generations, newCapacity);
            Array.Resize(ref _values, newCapacity);
        }
    }
}
