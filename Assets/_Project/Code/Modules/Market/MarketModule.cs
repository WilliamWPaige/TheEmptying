using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Modules.Market
{
    /// <summary>
    /// Prices, sales tax, personal shopping and bulk orders for every good.
    /// Sellers are simply any complete building's own inventory (a "shop") or
    /// a market building's rented stalls; there's no separate shop
    /// registration. Depends on Buildings (sellers, construction sites,
    /// market stalls), Wealth (payments and tax), Inventory (stock) and
    /// People (personal shopping) and map/movement (seller distance).
    /// </summary>
    public sealed class MarketModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.market";

        public const string StorageLimitPerBuildingSettingId = "setting.market.storage_limit_per_building";
        public const string SalesTaxPercentSettingId = "setting.market.sales_tax_percent";
        public const string MaxDailyPriceChangePercentSettingId = "setting.market.max_daily_price_change_percent";
        public const string PriceFloorPercentOfBaseSettingId = "setting.market.price_floor_percent_of_base";
        public const string PriceCeilingPercentOfBaseSettingId = "setting.market.price_ceiling_percent_of_base";
        public const string StallCountPerMarketSettingId = "setting.market.stall_count_per_market";
        public const string StallRentCopperPerMonthSettingId = "setting.market.stall_rent_copper_per_month";
        public const string LoavesWantedPerDaySettingId = "setting.market.loaves_wanted_per_day";
        public const string DeliveryHoursPer20CellsSettingId = "setting.market.delivery_hours_per_20_cells";

        public const string ShortageStartedEventId = "event.market.shortage_started";
        public const string ShortageEndedEventId = "event.market.shortage_ended";

        public const string MarketBuildingTypeId = "building.market";
        private const string LoafGoodId = "good.loaf";
        private const string FurnitureGoodId = "good.furniture";
        private const int PriceHistoryDays = 7;

        private sealed class HourlySystem : ISimSystem
        {
            private readonly MarketModule _owner;
            public HourlySystem(MarketModule owner) => _owner = owner;
            public string Id => ModuleId + ".hourly";
            public void Update(long tick) => _owner.DeliverArrivedOrders(tick);
        }

        private readonly BuildingsModule _buildings;
        private readonly WealthModule _wealth;
        private readonly InventoryModule _inventory;
        private readonly PeopleModule _people;
        private readonly MapMovementModule _mapMovement;

        private SettingsRegistry _settings;
        private EventBus _events;
        private Func<long> _currentTick;

        private Dictionary<string, Good> _goodsById;
        private Dictionary<string, MarketPriceState> _prices;
        private readonly Dictionary<EntityId, MarketBuildingState> _marketBuildings = new Dictionary<EntityId, MarketBuildingState>();
        private readonly List<PendingDelivery> _pendingDeliveries = new List<PendingDelivery>();

        /// <summary>Goods already ordered for a site but not yet delivered, so a daily re-check doesn't order the same shortfall twice.</summary>
        private readonly Dictionary<(EntityId Site, string GoodId), int> _onOrderForSite = new Dictionary<(EntityId, string), int>();

        public MarketModule(BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, MapMovementModule mapMovement)
        {
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            _wealth = wealth ?? throw new ArgumentNullException(nameof(wealth));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _mapMovement = mapMovement ?? throw new ArgumentNullException(nameof(mapMovement));
        }

        public string Id => ModuleId;
        public int Version => 1;

        public IReadOnlyList<string> Dependencies { get; } =
            new[] { BuildingsModule.ModuleId, WealthModule.ModuleId, InventoryModule.ModuleId, PeopleModule.ModuleId, MapMovementModule.ModuleId };

        public int SectionVersion => 1;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _currentTick = () => context.CurrentTick;

            LoadGoods(context.Content);

            context.Subscribe<NewDayEvent>(OnNewDay);
            context.Subscribe<NewMonthEvent>(OnNewMonth);
            context.RegisterSystem(new HourlySystem(this), CalendarConstants.TicksPerHour, 0, 0);

            _people.SetShoppingProvider(
                id => FindNearestSellerLocation(id, LoafGoodId),
                id => TryBuy(id, AccountId.Person(id), LoafGoodId, 1, out _, out _));

            RegisterCommands(context);
        }

        public void Shutdown()
        {
        }

        private void LoadGoods(ContentDatabase content)
        {
            _goodsById = new Dictionary<string, Good>();
            _prices = new Dictionary<string, MarketPriceState>();

            foreach (ContentEntry entry in content.ListByType(BuiltInContentTypes.GoodTypeName))
            {
                var good = (Good)entry.Definition;
                _goodsById[good.Id] = good;
                _prices[good.Id] = new MarketPriceState { CurrentPrice = good.BasePrice, PriceYesterday = good.BasePrice };
            }
        }

        #region Prices

        public IEnumerable<KeyValuePair<string, MarketPriceState>> Prices => _prices;

        public Money GetPrice(string goodId) => _prices.TryGetValue(goodId, out MarketPriceState state) ? state.CurrentPrice : Money.Zero;

        /// <summary>How much of a good is currently for sale anywhere (a shop's own inventory or a stall) - never counts the crown storehouse, since that's never sold to a private buyer.</summary>
        public int GetTotalStockForSale(string goodId) => TotalStockOf(goodId);

        /// <summary>
        /// 3.10b2: how much of a good a specific construction site could actually draw on next - the
        /// ordinary for-sale stock everyone can buy from, plus (for a crown site only) the crown
        /// storehouse's own stock, which TryDrawFromStorehouse delivers to a crown site directly and
        /// immediately but which a privately-owned site can never buy (FindSellers and TotalStockOf
        /// both skip the storehouse). A site's own "stalled" check and the building panel's "stuck"
        /// diagnostic both use this, instead of TotalStockOf/GetTotalStockForSale alone, so a crown
        /// site with stock sitting in its own storehouse never looks stalled when it isn't, and a
        /// private site waiting on stock only the crown holds never looks healthy when it can't
        /// actually get it.
        /// </summary>
        public int GetAvailableStockForSite(EntityId siteId, string goodId)
        {
            int stock = TotalStockOf(goodId);

            if (_buildings.TryGetBuilding(siteId, out BuildingState site) && !site.Owner.HasValue && _buildings.TryFindCrownStorehouse(out EntityId storehouseId))
            {
                stock += _inventory.GetCount(storehouseId, goodId);
            }

            return stock;
        }

        /// <summary>How much of a good is already on order (paid for, in transit) for a construction site - the building panel's materials-on-order line.</summary>
        public int GetOnOrder(EntityId siteId, string goodId) => _onOrderForSite.TryGetValue((siteId, goodId), out int amount) ? amount : 0;

        private int TotalStockOf(string goodId)
        {
            int total = 0;
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Buildings)
            {
                if (row.Value.Construction != null || row.Value.BuildingTypeId == BuildingsModule.CrownStorehouseTypeId)
                {
                    continue;
                }

                total += _inventory.GetCount(row.Key, goodId);
            }

            foreach (MarketBuildingState market in _marketBuildings.Values)
            {
                foreach (StallState stall in market.Stalls)
                {
                    total += stall.Stock.TryGetValue(goodId, out int count) ? count : 0;
                }
            }

            return total;
        }

        private void UpdatePricesAndShortages()
        {
            double maxChangePercent = _settings.Get(MaxDailyPriceChangePercentSettingId);
            double floorPercent = _settings.Get(PriceFloorPercentOfBaseSettingId);
            double ceilingPercent = _settings.Get(PriceCeilingPercentOfBaseSettingId);

            foreach (KeyValuePair<string, Good> goodRow in _goodsById)
            {
                string goodId = goodRow.Key;
                MarketPriceState state = _prices[goodId];
                int totalStockNow = TotalStockOf(goodId);

                Money price = state.CurrentPrice;
                if (state.BuyersTurnedAwayToday > 0)
                {
                    price = price.ApplyPercent(maxChangePercent);
                }
                else if (totalStockNow > state.TotalStockAtLastCheck)
                {
                    price = price.ApplyPercent(-maxChangePercent);
                }

                Money basePrice = goodRow.Value.BasePrice;
                Money floor = Money.FromHundredthsOfCopper((long)Math.Round(basePrice.HundredthsOfCopper * floorPercent / 100.0));
                Money ceiling = Money.FromHundredthsOfCopper((long)Math.Round(basePrice.HundredthsOfCopper * ceilingPercent / 100.0));
                if (price < floor)
                {
                    price = floor;
                }
                else if (price > ceiling)
                {
                    price = ceiling;
                }

                state.PriceYesterday = state.CurrentPrice;
                state.CurrentPrice = price;

                state.PriceHistory.Add(price);
                while (state.PriceHistory.Count > PriceHistoryDays)
                {
                    state.PriceHistory.RemoveAt(0);
                }

                bool unavailableNow = totalStockNow == 0;
                if (unavailableNow && state.WasUnavailableAtLastCheck && !state.InShortage)
                {
                    state.InShortage = true;
                    _events.Post(ShortageStartedEventId, _currentTick(), new EventData().SetString("good", goodId));
                }
                else if (!unavailableNow && state.InShortage)
                {
                    state.InShortage = false;
                    _events.Post(ShortageEndedEventId, _currentTick(), new EventData().SetString("good", goodId));
                }

                state.WasUnavailableAtLastCheck = unavailableNow;
                state.TotalStockAtLastCheck = totalStockNow;
                state.BuyersTurnedAwayToday = 0;
                state.UnitsSoldToday = 0;
            }
        }

        #endregion

        #region Sellers and stalls

        private readonly struct Seller
        {
            public readonly EntityId BuildingId;
            public readonly int StallIndex; // -1 for a shop (the building's own inventory)
            public readonly AccountId PayTo;
            public readonly GridPoint Location;
            public readonly int Stock;

            public Seller(EntityId buildingId, int stallIndex, AccountId payTo, GridPoint location, int stock)
            {
                BuildingId = buildingId;
                StallIndex = stallIndex;
                PayTo = payTo;
                Location = location;
                Stock = stock;
            }
        }

        private IEnumerable<Seller> FindSellers(string goodId)
        {
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Buildings)
            {
                if (row.Value.Construction != null || row.Value.BuildingTypeId == BuildingsModule.CrownStorehouseTypeId)
                {
                    continue;
                }

                int stock = _inventory.GetCount(row.Key, goodId);
                if (stock > 0)
                {
                    AccountId payTo = row.Value.Owner.HasValue ? AccountId.Person(row.Value.Owner.Value) : AccountId.Crown;
                    yield return new Seller(row.Key, -1, payTo, row.Value.Rect.Center, stock);
                }

                if (row.Value.BuildingTypeId == MarketBuildingTypeId && _marketBuildings.TryGetValue(row.Key, out MarketBuildingState market))
                {
                    for (int i = 0; i < market.Stalls.Count; i++)
                    {
                        StallState stall = market.Stalls[i];
                        int stallStock = stall.Stock.TryGetValue(goodId, out int c) ? c : 0;
                        if (stallStock > 0)
                        {
                            AccountId payTo = stall.Renter.HasValue ? AccountId.Person(stall.Renter.Value) : AccountId.Crown;
                            yield return new Seller(row.Key, i, payTo, row.Value.Rect.Center, stallStock);
                        }
                    }
                }
            }
        }

        private static double Distance(GridPoint a, GridPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Where a person should walk to buy a good right now - People's shopping-trip provider. Null if nobody's selling any.</summary>
        public GridPoint? FindNearestSellerLocation(EntityId buyer, string goodId)
        {
            GridPoint from = _mapMovement.IsMover(buyer) ? _mapMovement.GetCell(buyer) : default;
            return TryFindNearestSeller(goodId, from, out Seller seller) ? seller.Location : (GridPoint?)null;
        }

        private bool TryFindNearestSeller(string goodId, GridPoint from, out Seller nearest)
        {
            bool found = false;
            nearest = default;
            double bestDistance = double.MaxValue;

            foreach (Seller seller in FindSellers(goodId))
            {
                double distance = Distance(from, seller.Location);
                if (!found || distance < bestDistance)
                {
                    nearest = seller;
                    bestDistance = distance;
                    found = true;
                }
            }

            return found;
        }

        private void RemoveStockFromSeller(Seller seller, string goodId, int amount)
        {
            if (seller.StallIndex < 0)
            {
                _inventory.TryRemove(seller.BuildingId, goodId, amount);
            }
            else
            {
                MarketBuildingState market = _marketBuildings[seller.BuildingId];
                StallState stall = market.Stalls[seller.StallIndex];
                stall.Stock[goodId] = (stall.Stock.TryGetValue(goodId, out int c) ? c : 0) - amount;
            }
        }

        public MarketBuildingState GetOrCreateMarketBuilding(EntityId buildingId)
        {
            if (!_marketBuildings.TryGetValue(buildingId, out MarketBuildingState state))
            {
                state = new MarketBuildingState();
                int stallCount = (int)_settings.Get(StallCountPerMarketSettingId);
                for (int i = 0; i < stallCount; i++)
                {
                    state.Stalls.Add(new StallState());
                }

                _marketBuildings[buildingId] = state;
            }

            return state;
        }

        public bool TryGetMarketBuilding(EntityId buildingId, out MarketBuildingState state) => _marketBuildings.TryGetValue(buildingId, out state);

        /// <summary>Rents (renter.HasValue) or vacates (null) a stall. Anyone, including the crown (pass null), may hold one.</summary>
        public bool TryRentStall(EntityId marketBuildingId, int stallIndex, EntityId? renter, out string error)
        {
            if (!_buildings.TryGetBuilding(marketBuildingId, out BuildingState building) || building.BuildingTypeId != MarketBuildingTypeId || building.Construction != null)
            {
                error = "No such market.";
                return false;
            }

            MarketBuildingState market = GetOrCreateMarketBuilding(marketBuildingId);
            if (stallIndex < 0 || stallIndex >= market.Stalls.Count)
            {
                error = $"Usage: a stall index from 0 to {market.Stalls.Count - 1}.";
                return false;
            }

            StallState stall = market.Stalls[stallIndex];
            if (stall.Renter.HasValue && renter.HasValue && stall.Renter != renter)
            {
                error = "That stall is already rented.";
                return false;
            }

            stall.Renter = renter;
            if (!renter.HasValue)
            {
                stall.Stock.Clear();
            }

            error = null;
            return true;
        }

        /// <summary>Moves goods the renter already owns into their stall, ready for sale.</summary>
        public bool TryStockStall(EntityId marketBuildingId, int stallIndex, EntityId stocker, string goodId, int amount, out string error)
        {
            if (amount <= 0)
            {
                error = "Amount must be positive.";
                return false;
            }

            if (!_marketBuildings.TryGetValue(marketBuildingId, out MarketBuildingState market) || stallIndex < 0 || stallIndex >= market.Stalls.Count)
            {
                error = "No such stall.";
                return false;
            }

            StallState stall = market.Stalls[stallIndex];
            if (stall.Renter.HasValue && stall.Renter != stocker)
            {
                error = $"{stocker} doesn't rent that stall.";
                return false;
            }

            if (!_inventory.TryRemove(stocker, goodId, amount))
            {
                error = $"{stocker} doesn't have {amount} {goodId}.";
                return false;
            }

            stall.Stock[goodId] = (stall.Stock.TryGetValue(goodId, out int c) ? c : 0) + amount;
            error = null;
            return true;
        }

        #endregion

        #region Buying

        /// <summary>Sells up to count units of a good to the buyer from the nearest seller with stock, at the market price, tax withheld for the crown. Buys fewer than requested if that's all the nearest seller has or the buyer can afford; fails only when nothing at all is available or affordable.</summary>
        public bool TryBuy(EntityId buyer, AccountId buyerAccount, string goodId, int count, out int boughtCount, out string error)
        {
            boughtCount = 0;
            if (count <= 0)
            {
                error = "Count must be positive.";
                return false;
            }

            if (!_prices.TryGetValue(goodId, out MarketPriceState priceState))
            {
                error = $"No such good: {goodId}.";
                return false;
            }

            GridPoint from = _mapMovement.IsMover(buyer) ? _mapMovement.GetCell(buyer) : default;
            if (!TryFindNearestSeller(goodId, from, out Seller seller))
            {
                priceState.BuyersTurnedAwayToday++;
                error = $"No one is selling {goodId} right now.";
                return false;
            }

            Money price = priceState.CurrentPrice;
            int affordableCount = price.HundredthsOfCopper > 0 ? (int)(_wealth.GetBalance(buyerAccount).HundredthsOfCopper / price.HundredthsOfCopper) : count;
            int toBuy = Math.Min(count, Math.Min(seller.Stock, affordableCount));

            if (toBuy <= 0)
            {
                priceState.BuyersTurnedAwayToday++;
                error = affordableCount <= 0 ? $"{buyer} can't afford any {goodId}." : $"No {goodId} in stock nearby.";
                return false;
            }

            Money totalCost = price * toBuy;
            if (!_wealth.TryTransfer(buyerAccount, seller.PayTo, totalCost, TransferReason.Purchase))
            {
                priceState.BuyersTurnedAwayToday++;
                error = $"{buyer} can't afford {toBuy} {goodId}.";
                return false;
            }

            double taxPercent = _settings.Get(SalesTaxPercentSettingId);
            Money tax = Money.FromHundredthsOfCopper((long)Math.Round(totalCost.HundredthsOfCopper * taxPercent / 100.0));
            if (!seller.PayTo.IsCrown && tax.HundredthsOfCopper > 0)
            {
                _wealth.TryTransfer(seller.PayTo, AccountId.Crown, tax, TransferReason.Tax);
            }

            RemoveStockFromSeller(seller, goodId, toBuy);
            _inventory.Add(buyer, goodId, toBuy);

            priceState.UnitsSoldToday += toBuy;
            boughtCount = toBuy;

            if (toBuy < count)
            {
                priceState.BuyersTurnedAwayToday++;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// The crown as buyer of last resort: buys straight from a person's own
        /// stock (a hand gatherer with no shop or stall) at the market price,
        /// tax withheld, crediting the crown storehouse - not through
        /// FindSellers, since a person's own inventory is never a listed
        /// seller. Fails only if there's no crown storehouse yet, the person
        /// doesn't have the goods, or the treasury can't afford it.
        /// </summary>
        public bool TryCrownBuyFromPerson(EntityId seller, string goodId, int count, out int boughtCount, out string error)
        {
            boughtCount = 0;
            if (count <= 0)
            {
                error = "Count must be positive.";
                return false;
            }

            if (!_prices.TryGetValue(goodId, out MarketPriceState priceState))
            {
                error = $"No such good: {goodId}.";
                return false;
            }

            if (!_buildings.TryFindCrownStorehouse(out EntityId storehouseId))
            {
                error = "No crown storehouse exists yet.";
                return false;
            }

            int stock = _inventory.GetCount(seller, goodId);
            int toBuy = Math.Min(count, stock);
            if (toBuy <= 0)
            {
                error = $"{seller} has no {goodId} to sell.";
                return false;
            }

            Money price = priceState.CurrentPrice;
            Money totalCost = price * toBuy;
            if (!_wealth.TryTransfer(AccountId.Crown, AccountId.Person(seller), totalCost, TransferReason.Purchase))
            {
                error = "The treasury can't afford it.";
                return false;
            }

            double taxPercent = _settings.Get(SalesTaxPercentSettingId);
            Money tax = Money.FromHundredthsOfCopper((long)Math.Round(totalCost.HundredthsOfCopper * taxPercent / 100.0));
            if (tax.HundredthsOfCopper > 0)
            {
                _wealth.TryTransfer(AccountId.Person(seller), AccountId.Crown, tax, TransferReason.Tax);
            }

            _inventory.TryRemove(seller, goodId, toBuy);
            _inventory.Add(storehouseId, goodId, toBuy);

            priceState.UnitsSoldToday += toBuy;
            boughtCount = toBuy;
            error = null;
            return true;
        }

        #endregion

        #region Personal shopping

        private void RunPersonalShopping()
        {
            int loavesWantedPerDay = (int)_settings.Get(LoavesWantedPerDaySettingId);
            int loafTarget = loavesWantedPerDay * 2;

            foreach (KeyValuePair<EntityId, PersonState> row in _people.People.ToList())
            {
                EntityId person = row.Key;
                AccountId account = AccountId.Person(person);

                int loaves = _inventory.GetCount(person, LoafGoodId);
                if (loaves < loafTarget)
                {
                    TryBuy(person, account, LoafGoodId, loafTarget - loaves, out _, out _);
                }

                SocialClass personClass = _wealth.GetClass(person);
                if (personClass != SocialClass.Lower && _inventory.GetCount(person, FurnitureGoodId) < 1)
                {
                    TryBuy(person, account, FurnitureGoodId, 1, out _, out _);
                }
            }
        }

        #endregion

        #region Bulk orders

        private void RunBulkOrders(long tick)
        {
            double hoursPer20Cells = _settings.Get(DeliveryHoursPer20CellsSettingId);

            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Buildings.ToList())
            {
                EntityId siteId = row.Key;
                BuildingState site = row.Value;
                if (site.Construction == null)
                {
                    continue;
                }

                BuildingType type = _buildings.GetBuildingType(site.BuildingTypeId);
                if (type == null)
                {
                    continue;
                }

                foreach (GoodAmount cost in type.ConstructionCost)
                {
                    string goodId = cost.Good.Id;
                    int delivered = site.Construction.DeliveredGoods.TryGetValue(goodId, out int d) ? d : 0;
                    int onOrder = _onOrderForSite.TryGetValue((siteId, goodId), out int o) ? o : 0;
                    int stillNeeded = cost.Amount - delivered - onOrder;
                    if (stillNeeded <= 0)
                    {
                        continue;
                    }

                    // 3.10b3: a crown site's own storehouse stock no longer moves here on its own - a
                    // founder hauls it over by hand (GatheringModule). This bulk-order pass still buys
                    // the shortfall from a private seller (and delivers it after a distance delay) for
                    // any site, crown included, exactly as before; only the stockpile-to-site leg changed.
                    AccountId buyerAccount = site.Owner.HasValue ? AccountId.Person(site.Owner.Value) : AccountId.Crown;
                    GridPoint from = site.Rect.Center;

                    if (!TryFindNearestSeller(goodId, from, out Seller seller))
                    {
                        continue;
                    }

                    Money price = _prices[goodId].CurrentPrice;
                    int affordableCount = price.HundredthsOfCopper > 0 ? (int)(_wealth.GetBalance(buyerAccount).HundredthsOfCopper / price.HundredthsOfCopper) : stillNeeded;
                    int toOrder = Math.Min(stillNeeded, Math.Min(seller.Stock, affordableCount));
                    if (toOrder <= 0)
                    {
                        continue;
                    }

                    Money totalCost = price * toOrder;
                    if (!_wealth.TryTransfer(buyerAccount, seller.PayTo, totalCost, TransferReason.Purchase))
                    {
                        continue;
                    }

                    double taxPercent = _settings.Get(SalesTaxPercentSettingId);
                    Money tax = Money.FromHundredthsOfCopper((long)Math.Round(totalCost.HundredthsOfCopper * taxPercent / 100.0));
                    if (!seller.PayTo.IsCrown && tax.HundredthsOfCopper > 0)
                    {
                        _wealth.TryTransfer(seller.PayTo, AccountId.Crown, tax, TransferReason.Tax);
                    }

                    RemoveStockFromSeller(seller, goodId, toOrder);
                    _prices[goodId].UnitsSoldToday += toOrder;

                    double distance = Distance(from, seller.Location);
                    long delayTicks = (long)Math.Ceiling(distance / 20.0 * hoursPer20Cells * CalendarConstants.TicksPerHour);
                    _pendingDeliveries.Add(new PendingDelivery { ArrivalTick = tick + Math.Max(1, delayTicks), Buyer = siteId, GoodId = goodId, Amount = toOrder });
                    _onOrderForSite[(siteId, goodId)] = onOrder + toOrder;
                }
            }
        }

        private void DeliverArrivedOrders(long tick)
        {
            if (_pendingDeliveries.Count == 0)
            {
                return;
            }

            for (int i = _pendingDeliveries.Count - 1; i >= 0; i--)
            {
                PendingDelivery delivery = _pendingDeliveries[i];
                if (delivery.ArrivalTick > tick)
                {
                    continue;
                }

                if (_buildings.TryGetBuilding(delivery.Buyer, out BuildingState site) && site.Construction != null)
                {
                    EntityId? source = site.Owner;
                    if (!source.HasValue && _buildings.TryFindCrownStorehouse(out EntityId storehouseId))
                    {
                        // Credit the storehouse, then draw it right back out - the storehouse stays the single source of truth for crown goods.
                        source = storehouseId;
                    }

                    if (source.HasValue)
                    {
                        _inventory.Add(source.Value, delivery.GoodId, delivery.Amount);
                        _buildings.TryDeliverGoods(delivery.Buyer, delivery.GoodId, delivery.Amount, out _);
                    }

                    // No owner and no storehouse to receive it: the order was already paid for and the goods are lost (placeholder, matches "with no goods and no money they stall").
                }

                var key = (delivery.Buyer, delivery.GoodId);
                if (_onOrderForSite.TryGetValue(key, out int onOrder))
                {
                    int remaining = onOrder - delivery.Amount;
                    if (remaining > 0)
                    {
                        _onOrderForSite[key] = remaining;
                    }
                    else
                    {
                        _onOrderForSite.Remove(key);
                    }
                }

                _pendingDeliveries.RemoveAt(i);
            }
        }

        #endregion

        #region Monthly stall rent

        private void OnNewMonth(NewMonthEvent evt)
        {
            Money rent = Money.FromCopper((long)_settings.Get(StallRentCopperPerMonthSettingId));

            foreach (MarketBuildingState market in _marketBuildings.Values)
            {
                foreach (StallState stall in market.Stalls)
                {
                    if (!stall.Renter.HasValue)
                    {
                        continue;
                    }

                    if (!_wealth.TryTransfer(AccountId.Person(stall.Renter.Value), AccountId.Crown, rent, TransferReason.Rent))
                    {
                        stall.Renter = null;
                        stall.Stock.Clear();
                    }
                }
            }
        }

        #endregion

        #region Daily tick

        private void OnNewDay(NewDayEvent evt)
        {
            RunPersonalShopping();
            RunBulkOrders(evt.Tick);
            UpdatePricesAndShortages();
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("market", "market - lists every good's price, change since yesterday, total stock and recent demand.", args =>
            {
                var lines = new List<string>();
                foreach (KeyValuePair<string, MarketPriceState> row in _prices.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    Money change = row.Value.CurrentPrice - row.Value.PriceYesterday;
                    int stock = TotalStockOf(row.Key);
                    lines.Add($"{row.Key}: {row.Value.CurrentPrice} ({(change.HundredthsOfCopper >= 0 ? "+" : "")}{change} since yesterday), stock {stock}, sold {row.Value.UnitsSoldToday}, turned away {row.Value.BuyersTurnedAwayToday}" + (row.Value.InShortage ? " [SHORTAGE]" : ""));
                }

                return lines.Count == 0 ? "No goods." : string.Join("\n", lines);
            }));

            context.RegisterCommand(new ConsoleCommand("good", "good <goodId> - lists sellers and the last 7 days' prices for one good.", args =>
            {
                if (args.Length != 1 || !_prices.TryGetValue(args[0], out MarketPriceState state))
                {
                    return "Usage: good <goodId> (e.g. good.loaf)";
                }

                var lines = new List<string> { $"Price: {state.CurrentPrice}", $"History: {string.Join(", ", state.PriceHistory.Select(p => p.ToString()))}" };
                foreach (Seller seller in FindSellers(args[0]))
                {
                    lines.Add(seller.StallIndex < 0
                        ? $"  shop {seller.BuildingId}: {seller.Stock} in stock"
                        : $"  stall {seller.BuildingId}#{seller.StallIndex}: {seller.Stock} in stock");
                }

                return string.Join("\n", lines);
            }));

            context.RegisterCommand(new ConsoleCommand("buy", "buy <personId> <goodId> <count> - buys from the nearest seller, for testing.", args =>
            {
                if (args.Length != 3 || !EntityId.TryParse(args[0], out EntityId personId) || !ConsoleArgs.TryParseInt(args[2], out int count))
                {
                    return "Usage: buy <personId> <goodId> <count>";
                }

                return TryBuy(personId, AccountId.Person(personId), args[1], count, out int bought, out string error)
                    ? $"Bought {bought} {args[1]} for {personId}."
                    : error;
            }));

            context.RegisterCommand(new ConsoleCommand("crownbuy", "crownbuy <personId> <goodId> <count> - the crown buys straight from a person's own stock, for testing.", args =>
            {
                if (args.Length != 3 || !EntityId.TryParse(args[0], out EntityId personId) || !ConsoleArgs.TryParseInt(args[2], out int count))
                {
                    return "Usage: crownbuy <personId> <goodId> <count>";
                }

                return TryCrownBuyFromPerson(personId, args[1], count, out int bought, out string error)
                    ? $"The crown bought {bought} {args[1]} from {personId}."
                    : error;
            }));
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            List<string> goodIds = _prices.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            writer.WriteStringList(goodIds);
            foreach (string goodId in goodIds)
            {
                MarketPriceState state = _prices[goodId];
                writer.WriteLong(state.CurrentPrice.HundredthsOfCopper);
                writer.WriteLong(state.PriceYesterday.HundredthsOfCopper);
                writer.WriteLongList(state.PriceHistory.Select(p => p.HundredthsOfCopper).ToList());
                writer.WriteInt(state.TotalStockAtLastCheck);
                writer.WriteInt(state.BuyersTurnedAwayToday);
                writer.WriteInt(state.UnitsSoldToday);
                writer.WriteBool(state.WasUnavailableAtLastCheck);
                writer.WriteBool(state.InShortage);
            }

            List<EntityId> marketIds = _marketBuildings.Keys.OrderBy(id => id.Index).ToList();
            writer.WriteInt(marketIds.Count);
            foreach (EntityId marketId in marketIds)
            {
                writer.WriteEntityId(marketId);
                MarketBuildingState market = _marketBuildings[marketId];
                writer.WriteInt(market.Stalls.Count);
                foreach (StallState stall in market.Stalls)
                {
                    writer.WriteBool(stall.Renter.HasValue);
                    if (stall.Renter.HasValue)
                    {
                        writer.WriteEntityId(stall.Renter.Value);
                    }

                    List<string> stockGoodIds = stall.Stock.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                    writer.WriteInt(stockGoodIds.Count);
                    foreach (string goodId in stockGoodIds)
                    {
                        writer.WriteString(goodId);
                        writer.WriteInt(stall.Stock[goodId]);
                    }
                }
            }

            writer.WriteInt(_pendingDeliveries.Count);
            foreach (PendingDelivery delivery in _pendingDeliveries)
            {
                writer.WriteLong(delivery.ArrivalTick);
                writer.WriteEntityId(delivery.Buyer);
                writer.WriteString(delivery.GoodId);
                writer.WriteInt(delivery.Amount);
            }

            List<(EntityId Site, string GoodId)> onOrderKeys = _onOrderForSite.Keys.OrderBy(k => k.Site.Index).ThenBy(k => k.GoodId, StringComparer.Ordinal).ToList();
            writer.WriteInt(onOrderKeys.Count);
            foreach ((EntityId Site, string GoodId) key in onOrderKeys)
            {
                writer.WriteEntityId(key.Site);
                writer.WriteString(key.GoodId);
                writer.WriteInt(_onOrderForSite[key]);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            foreach (string goodId in reader.ReadStringList())
            {
                if (!_prices.TryGetValue(goodId, out MarketPriceState state))
                {
                    state = new MarketPriceState();
                    _prices[goodId] = state;
                }

                state.CurrentPrice = Money.FromHundredthsOfCopper(reader.ReadLong());
                state.PriceYesterday = Money.FromHundredthsOfCopper(reader.ReadLong());
                state.PriceHistory = reader.ReadLongList().Select(Money.FromHundredthsOfCopper).ToList();
                state.TotalStockAtLastCheck = reader.ReadInt();
                state.BuyersTurnedAwayToday = reader.ReadInt();
                state.UnitsSoldToday = reader.ReadInt();
                state.WasUnavailableAtLastCheck = reader.ReadBool();
                state.InShortage = reader.ReadBool();
            }

            int marketCount = reader.ReadInt();
            for (int i = 0; i < marketCount; i++)
            {
                EntityId marketId = reader.ReadEntityId();
                var market = new MarketBuildingState();
                int stallCount = reader.ReadInt();
                for (int s = 0; s < stallCount; s++)
                {
                    var stall = new StallState();
                    if (reader.ReadBool())
                    {
                        stall.Renter = reader.ReadEntityId();
                    }

                    int stockCount = reader.ReadInt();
                    for (int g = 0; g < stockCount; g++)
                    {
                        string goodId = reader.ReadString();
                        stall.Stock[goodId] = reader.ReadInt();
                    }

                    market.Stalls.Add(stall);
                }

                _marketBuildings[marketId] = market;
            }

            int pendingCount = reader.ReadInt();
            for (int i = 0; i < pendingCount; i++)
            {
                _pendingDeliveries.Add(new PendingDelivery
                {
                    ArrivalTick = reader.ReadLong(),
                    Buyer = reader.ReadEntityId(),
                    GoodId = reader.ReadString(),
                    Amount = reader.ReadInt()
                });
            }

            int onOrderCount = reader.ReadInt();
            for (int i = 0; i < onOrderCount; i++)
            {
                EntityId site = reader.ReadEntityId();
                string goodId = reader.ReadString();
                _onOrderForSite[(site, goodId)] = reader.ReadInt();
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (_marketBuildings.TryGetValue(entity, out MarketBuildingState market))
            {
                for (int i = 0; i < market.Stalls.Count; i++)
                {
                    StallState stall = market.Stalls[i];
                    string renter = stall.Renter.HasValue ? stall.Renter.Value.ToString() : "vacant";
                    string stock = stall.Stock.Count == 0 ? "empty" : string.Join(", ", stall.Stock.Select(kv => $"{kv.Value} {kv.Key}"));
                    yield return $"stall #{i}: {renter}, {stock}";
                }

                yield break;
            }

            if (_buildings.TryGetBuilding(entity, out BuildingState building) && building.Construction == null)
            {
                var goods = _inventory.GetAll(entity).ToList();
                if (goods.Count > 0)
                {
                    yield return "for sale: " + string.Join(", ", goods.Select(kv => $"{kv.Value} {kv.Key}"));
                }
            }
        }

        #endregion
    }
}
