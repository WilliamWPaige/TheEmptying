using System.Collections.Generic;

namespace TheEmptying.Modules.Inventory
{
    /// <summary>One entity's goods: good Id to whole-number count.</summary>
    public sealed class InventoryState
    {
        public readonly Dictionary<string, int> Goods = new Dictionary<string, int>();
    }
}
