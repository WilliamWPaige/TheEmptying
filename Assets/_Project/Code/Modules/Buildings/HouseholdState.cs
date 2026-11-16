using TheEmptying.Core.Entities;

namespace TheEmptying.Modules.Buildings
{
    /// <summary>One home inside a residential building or crown lodging.</summary>
    public sealed class HouseholdState
    {
        public EntityId? Occupant;
        public bool IsOwnerOccupied;
    }
}
