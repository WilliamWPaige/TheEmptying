using System.Collections.Generic;
using TheEmptying.Core.Economy;

namespace TheEmptying.Modules.Market
{
    /// <summary>One good's market price and the signals that move it. Module state: prices change during play, so they are never settings.</summary>
    public sealed class MarketPriceState
    {
        public Money CurrentPrice;
        public Money PriceYesterday;

        /// <summary>The last 7 daily closing prices, oldest first.</summary>
        public List<Money> PriceHistory = new List<Money>();

        /// <summary>Total stock across every seller, snapshotted at today's price check, for tomorrow's "building up" comparison.</summary>
        public int TotalStockAtLastCheck;

        /// <summary>Buyers turned away today for lack of stock at an affordable price; reset at the next price check.</summary>
        public int BuyersTurnedAwayToday;

        /// <summary>Units sold today, for "recent demand"; reset at the next price check.</summary>
        public int UnitsSoldToday;

        /// <summary>Whether this good had zero stock everywhere at the previous check - one more all-day zero confirms a shortage.</summary>
        public bool WasUnavailableAtLastCheck;

        public bool InShortage;
    }
}
