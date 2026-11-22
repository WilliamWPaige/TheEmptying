using System;

namespace TheEmptying.Modules.Map
{
    /// <summary>A square chunk of the world grid (R6), chunk_size_meters per side.</summary>
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public int X { get; }
        public int Z { get; }

        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Z;
            }
        }

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);

        public override string ToString() => $"({X}, {Z})";
    }
}
