using System;
using System.Globalization;

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

        /// <summary>Parses the "eIndex.Generation" form produced by ToString, e.g. "e12.3".</summary>
        public static bool TryParse(string text, out EntityId id)
        {
            id = None;

            if (string.IsNullOrEmpty(text) || text[0] != 'e')
            {
                return false;
            }

            int dot = text.IndexOf('.');
            if (dot < 1)
            {
                return false;
            }

            string indexPart = text.Substring(1, dot - 1);
            string generationPart = text.Substring(dot + 1);

            if (!int.TryParse(indexPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                !int.TryParse(generationPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation))
            {
                return false;
            }

            id = new EntityId(index, generation);
            return true;
        }
    }
}
