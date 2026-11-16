using TheEmptying.Core.Entities;

namespace TheEmptying.Modules.MapMovement
{
    /// <summary>A queued path search: find a route for Mover from Start to Goal.</summary>
    public readonly struct PathRequest
    {
        public EntityId Mover { get; }
        public GridPoint Start { get; }
        public GridPoint Goal { get; }

        public PathRequest(EntityId mover, GridPoint start, GridPoint goal)
        {
            Mover = mover;
            Start = start;
            Goal = goal;
        }
    }
}
