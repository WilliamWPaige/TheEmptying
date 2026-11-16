using System;

namespace TheEmptying.Modules.MapMovement
{
    /// <summary>A cell coordinate on the flat grid map.</summary>
    public readonly struct GridPoint : IEquatable<GridPoint>
    {
        public int X { get; }
        public int Y { get; }

        public GridPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(GridPoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static bool operator ==(GridPoint a, GridPoint b) => a.Equals(b);
        public static bool operator !=(GridPoint a, GridPoint b) => !a.Equals(b);

        public override string ToString() => $"({X}, {Y})";
    }
}
