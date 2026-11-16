using System;
using TheEmptying.Core.Randomness;

namespace TheEmptying.Modules.MapMovement
{
    /// <summary>
    /// The flat grid: cell size, walkability and movement cost per cell, and
    /// an occupancy layer other modules (buildings later) use to block cells
    /// without touching the terrain data underneath.
    /// </summary>
    public sealed class MapGrid
    {
        public int Width { get; }
        public int Depth { get; }
        public double CellSizeMeters { get; }

        private readonly bool[] _walkable;
        private readonly float[] _movementCost;
        private readonly bool[] _occupied;

        public MapGrid(int width, int depth, double cellSizeMeters)
        {
            if (width <= 0 || depth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions must be positive.");
            }

            Width = width;
            Depth = depth;
            CellSizeMeters = cellSizeMeters;

            int cellCount = width * depth;
            _walkable = new bool[cellCount];
            _movementCost = new float[cellCount];
            _occupied = new bool[cellCount];

            for (int i = 0; i < cellCount; i++)
            {
                _walkable[i] = true;
                _movementCost[i] = 1f;
            }
        }

        public bool InBounds(GridPoint cell) => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Depth;

        public bool IsWalkable(GridPoint cell) => InBounds(cell) && _walkable[IndexOf(cell)];
        public void SetWalkable(GridPoint cell, bool walkable) => _walkable[IndexOf(cell)] = walkable;

        public float GetMovementCost(GridPoint cell) => _movementCost[IndexOf(cell)];
        public void SetMovementCost(GridPoint cell, float cost) => _movementCost[IndexOf(cell)] = cost;

        public bool IsOccupied(GridPoint cell) => InBounds(cell) && _occupied[IndexOf(cell)];
        public void SetOccupied(GridPoint cell, bool occupied) => _occupied[IndexOf(cell)] = occupied;

        /// <summary>Walkable, in bounds, and not occupied - the condition pathfinding and movement both use.</summary>
        public bool IsPassable(GridPoint cell) => InBounds(cell) && _walkable[IndexOf(cell)] && !_occupied[IndexOf(cell)];

        public int IndexOf(GridPoint cell) => cell.Y * Width + cell.X;

        public (double X, double Y) CellCenterWorldPosition(GridPoint cell) =>
            ((cell.X + 0.5) * CellSizeMeters, (cell.Y + 0.5) * CellSizeMeters);

        public GridPoint WorldToCell(double x, double y)
        {
            int cellX = Math.Clamp((int)(x / CellSizeMeters), 0, Width - 1);
            int cellY = Math.Clamp((int)(y / CellSizeMeters), 0, Depth - 1);
            return new GridPoint(cellX, cellY);
        }

        /// <summary>A uniformly random passable cell, or null if none exists (an entirely blocked/occupied map).</summary>
        public GridPoint? RandomPassableCell(SimRandom random)
        {
            int cellCount = Width * Depth;
            int startIndex = random.NextInt(0, cellCount);

            for (int offset = 0; offset < cellCount; offset++)
            {
                int index = (startIndex + offset) % cellCount;
                if (_walkable[index] && !_occupied[index])
                {
                    return new GridPoint(index % Width, index / Width);
                }
            }

            return null;
        }
    }
}
