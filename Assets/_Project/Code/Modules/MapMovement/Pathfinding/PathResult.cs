using System.Collections.Generic;

namespace TheEmptying.Modules.MapMovement.Pathfinding
{
    /// <summary>The result of a path search: found, with cells from start to goal inclusive, or not found - a clear result, not an error.</summary>
    public readonly struct PathResult
    {
        public bool Found { get; }
        public IReadOnlyList<GridPoint> Cells { get; }

        private PathResult(bool found, IReadOnlyList<GridPoint> cells)
        {
            Found = found;
            Cells = cells;
        }

        public static PathResult Success(IReadOnlyList<GridPoint> cells) => new PathResult(true, cells);
        public static readonly PathResult NoPath = new PathResult(false, System.Array.Empty<GridPoint>());
    }
}
