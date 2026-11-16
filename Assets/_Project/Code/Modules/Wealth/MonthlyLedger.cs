using System.Collections.Generic;
using TheEmptying.Core.Economy;

namespace TheEmptying.Modules.Wealth
{
    /// <summary>One month's totals in and out, by reason category, for one account.</summary>
    public sealed class MonthlyLedger
    {
        public readonly Dictionary<TransferReason, Money> In = new Dictionary<TransferReason, Money>();
        public readonly Dictionary<TransferReason, Money> Out = new Dictionary<TransferReason, Money>();

        public void RecordIn(TransferReason reason, Money amount)
        {
            In[reason] = In.TryGetValue(reason, out Money existing) ? existing + amount : amount;
        }

        public void RecordOut(TransferReason reason, Money amount)
        {
            Out[reason] = Out.TryGetValue(reason, out Money existing) ? existing + amount : amount;
        }
    }
}
