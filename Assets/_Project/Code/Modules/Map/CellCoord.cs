using System;

namespace TheEmptying.Modules.Map
{
    /// <summary>A 1-meter cell on the world grid (R6). X/Z match the ground plane's own axes.</summary>
    public readonly struct CellCoord : IEquatable<CellCoord>
    {
        public int X { get; }
        public int Z { get; }

        public CellCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(CellCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is CellCoord other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Z;
            }
        }

        public static bool operator ==(CellCoord a, CellCoord b) => a.Equals(b);
        public static bool operator !=(CellCoord a, CellCoord b) => !a.Equals(b);

        public override string ToString() => $"({X}, {Z})";
    }
}
