using System.Collections.Generic;
using TheEmptying.Core.Entities;
using TheEmptying.Modules.Land;

namespace TheEmptying.Modules.Buildings
{
    /// <summary>
    /// One building instance, from the moment it's placed (a construction
    /// site) through completion. Private buildings carry an Owner and the
    /// plot they stand on; government buildings carry neither (null means
    /// the crown).
    /// </summary>
    public sealed class BuildingState
    {
        public string BuildingTypeId;
        public CellRect Rect;
        public EntityId? Owner;
        public EntityId? PlotId;

        /// <summary>Null once the building is finished.</summary>
        public ConstructionState Construction;

        /// <summary>Empty until the building is finished and HouseholdsProvided &gt; 0.</summary>
        public List<HouseholdState> Households = new List<HouseholdState>();
    }
}
