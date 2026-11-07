using System;

namespace TheEmptying.Core.Entities
{
    /// <summary>
    /// An index plus a generation, so a reused slot can never be confused with an
    /// old entity that lived there.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public static readonly EntityId None = new EntityId(-1, 0);

        public int Index { get; }
        public int Generation { get; }

        public EntityId(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsNone => Index < 0;

        public bool Equals(EntityId other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ Generation;
            }
        }

        public override string ToString()
        {
            return IsNone ? "e.none" : $"e{Index}.{Generation}";
        }

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }
}
