using TheEmptying.Core.Entities;

namespace TheEmptying.Modules.Market
{
    /// <summary>A bulk order already paid for, in transit: no hauler job, just a distance-based delay before the goods appear in the buyer's inventory.</summary>
    public sealed class PendingDelivery
    {
        public long ArrivalTick;
        public EntityId Buyer;
        public string GoodId;
        public int Amount;
    }
}
