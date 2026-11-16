using System.Collections.Generic;

namespace TheEmptying.Modules.MapMovement
{
    /// <summary>
    /// Everything about one mover: its exact position (meters), the cells
    /// still ahead of it on its current path, its destination cell, and
    /// whether a new path has been requested but not resolved yet.
    /// </summary>
    public sealed class MoverState
    {
        public double X;
        public double Y;

        /// <summary>
        /// Remaining cells to walk through, nearest first (a queue so stepping
        /// off the front cell is O(1), not a List's O(n) shift). Does not
        /// include the mover's current cell.
        /// </summary>
        public readonly Queue<GridPoint> Path = new Queue<GridPoint>();

        public GridPoint? Destination;
        public bool HasPendingRequest;
    }
}
