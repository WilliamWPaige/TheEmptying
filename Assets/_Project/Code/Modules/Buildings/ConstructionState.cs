using System.Collections.Generic;

namespace TheEmptying.Modules.Buildings
{
    /// <summary>Goods delivered so far (by good id) and labor hours done, for a building not yet complete.</summary>
    public sealed class ConstructionState
    {
        public readonly Dictionary<string, int> DeliveredGoods = new Dictionary<string, int>();
        public double LaborHoursDone;
    }
}
