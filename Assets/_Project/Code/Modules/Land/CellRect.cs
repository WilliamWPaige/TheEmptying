using System.Collections.Generic;
using TheEmptying.Modules.MapMovement;

namespace TheEmptying.Modules.Land
{
    /// <summary>A rectangle of grid cells, origin at the top-left corner.</summary>
    public readonly struct CellRect
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public CellRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public GridPoint Center => new GridPoint(X + Width / 2, Y + Height / 2);

        public bool Contains(GridPoint cell) => cell.X >= X && cell.X < X + Width && cell.Y >= Y && cell.Y < Y + Height;

        public bool Overlaps(CellRect other) =>
            X < other.X + other.Width && other.X < X + Width &&
            Y < other.Y + other.Height && other.Y < Y + Height;

        public IEnumerable<GridPoint> Cells()
        {
            for (int y = Y; y < Y + Height; y++)
            {
                for (int x = X; x < X + Width; x++)
                {
                    yield return new GridPoint(x, y);
                }
            }
        }
    }
}
