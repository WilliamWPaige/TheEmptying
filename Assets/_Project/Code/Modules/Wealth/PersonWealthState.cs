using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;

namespace TheEmptying.Modules.Wealth
{
    /// <summary>One person's money account, class and the data that feeds the monthly class check.</summary>
    public sealed class PersonWealthState
    {
        public Money Balance = Money.Zero;
        public MonthlyLedger Current = new MonthlyLedger();
        public MonthlyLedger Previous = new MonthlyLedger();

        public SocialClass Class = SocialClass.Lower;

        /// <summary>Consecutive monthly checks, in a row, where the qualifying tier was below Class. Reset to 0 whenever it isn't.</summary>
        public int MonthsBelowClass;

        /// <summary>Placeholder: random drive to climb. 0 means content to stay where they are. Town growth (3.11) reads this; it never feeds the class check itself.</summary>
        public double Ambition;

        /// <summary>No births yet; set directly (SetParent) for the child-inherits-class hook.</summary>
        public EntityId? ParentId;
    }
}
