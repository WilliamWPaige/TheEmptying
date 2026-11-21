using TheEmptying.Modules.MapMovement;

namespace TheEmptying.Modules.MapResources
{
    /// <summary>One standing tree or rock outcrop: where it is and how much it still holds.</summary>
    public sealed class ResourceNodeState
    {
        public ResourceNodeKind Kind;
        public GridPoint Cell;
        public int RemainingStock;
    }
}
