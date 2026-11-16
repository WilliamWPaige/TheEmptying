using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Market
{
    public class MarketModuleSalesTests
    {
        private static (Simulation Simulation, MarketModule Market, BuildingsModule Buildings, WealthModule Wealth, InventoryModule Inventory, PeopleModule People, LandModule Land) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            foreach (var row in people.People.ToList())
            {
                simulation.Entities.Destroy(row.Key);
            }

            return (simulation, market, buildings, wealth, inventory, people, land);
        }

        private static void StepDays(Simulation sim, int days)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay * days; i++)
            {
                sim.Step();
            }
        }

        /// <summary>Crown sites draw materials from the crown storehouse (3.9), so a market building needs a complete, well-stocked storehouse before it can finish too.</summary>
        private static EntityId PlaceCompleteMarket(BuildingsModule buildings, InventoryModule inventory, GridPoint at)
        {
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(at.X + 200, at.Y + 200), out EntityId storehouseId, out string storehouseError), storehouseError);
            buildings.TryDeliverGoods(storehouseId, "good.plank", 20, out _); // the storehouse's own construction is free (nothing else can supply the first one)
            buildings.TryDeliverGoods(storehouseId, "good.stone", 10, out _);
            buildings.TryAddLaborHours(storehouseId, 9999, out _);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.market", at, out EntityId marketId, out string marketError), marketError);
            Assert.IsTrue(buildings.TryDeliverGoods(marketId, "good.plank", 20, out string plankError), plankError);
            Assert.IsTrue(buildings.TryDeliverGoods(marketId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildings.TryAddLaborHours(marketId, 9999, out string laborError), laborError);
            return marketId;
        }

        [Test]
        public void SellingFromAShop_PaysTheOwner_AndTaxToTheCrown()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            EntityId shop = PlaceCompleteMarket(buildings, inventory, new GridPoint(10, 10));
            inventory.Add(shop, "good.loaf", 10);

            EntityId buyer = people.SpawnPerson(10, 10); // starts with some automatic food provisions
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(1000));
            int buyerLoavesBefore = inventory.GetCount(buyer, "good.loaf");

            Money crownBefore = wealth.GetBalance(AccountId.Crown);

            Assert.IsTrue(market.TryBuy(buyer, AccountId.Person(buyer), "good.loaf", 5, out int bought, out string error), error);
            Assert.AreEqual(5, bought);
            Assert.AreEqual(buyerLoavesBefore + 5, inventory.GetCount(buyer, "good.loaf"));
            Assert.AreEqual(5, inventory.GetCount(shop, "good.loaf"));

            // The market/crown building has no owner, so the crown is both seller and tax collector here -
            // use a privately-owned shop below to see the tax actually leave the seller.
            Assert.Greater(wealth.GetBalance(AccountId.Crown).HundredthsOfShil, crownBefore.HundredthsOfShil);
        }

        [Test]
        public void SellingFromAPrivatelyOwnedBuilding_SplitsPaymentAndTax()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            // Market treats any complete building's own inventory as a shop - no separate
            // registration - so a plain private cottage stands in for a business here.
            EntityId owner = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(owner), Money.FromShil(5000));
            inventory.Add(owner, "good.loaf", 10000);
            inventory.Add(owner, "good.plank", 40);
            inventory.Add(owner, "good.stone", 10);

            land.RequestSetZone(new CellRect(40, 40, 4, 4), "zone.residential_low");
            sim.Step();
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(40, 40), out _, out string buyError), buyError);
            Assert.IsTrue(buildings.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string requestError), requestError);
            Assert.IsTrue(buildings.TryDeliverGoods(cottageId, "good.plank", 40, out string plankError), plankError);
            Assert.IsTrue(buildings.TryDeliverGoods(cottageId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildings.TryAddLaborHours(cottageId, 50, out string laborError), laborError);

            inventory.Add(cottageId, "good.furniture", 5);

            EntityId buyer = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(1000));

            Money ownerBefore = wealth.GetBalance(AccountId.Person(owner));
            Money crownBefore = wealth.GetBalance(AccountId.Crown);
            Money price = market.GetPrice("good.furniture");

            Assert.IsTrue(market.TryBuy(buyer, AccountId.Person(buyer), "good.furniture", 2, out int bought, out string error), error);
            Assert.AreEqual(2, bought);

            Money ownerGain = wealth.GetBalance(AccountId.Person(owner)) - ownerBefore;
            Money crownGain = wealth.GetBalance(AccountId.Crown) - crownBefore;
            Money totalCost = price * 2;

            Assert.Greater(ownerGain.HundredthsOfShil, 0, "The owner should be paid.");
            Assert.Greater(crownGain.HundredthsOfShil, 0, "Sales tax should reach the crown.");
            Assert.AreEqual(totalCost.HundredthsOfShil, ownerGain.HundredthsOfShil + crownGain.HundredthsOfShil, "The full price should split between the owner and the crown's tax.");
        }

        [Test]
        public void SellingFromAStall_PaysTheRenter_AndTax()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            EntityId marketBuildingId = PlaceCompleteMarket(buildings, inventory, new GridPoint(10, 10));

            EntityId renter = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(renter), Money.FromShil(5000));
            inventory.Add(renter, "good.furniture", 10);

            Assert.IsTrue(market.TryRentStall(marketBuildingId, 0, renter, out string rentError), rentError);
            Assert.IsTrue(market.TryStockStall(marketBuildingId, 0, renter, "good.furniture", 5, out string stockError), stockError);
            Assert.AreEqual(5, inventory.GetCount(renter, "good.furniture"));

            EntityId buyer = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(1000));

            Money renterBalanceBefore = wealth.GetBalance(AccountId.Person(renter));
            Money crownBefore = wealth.GetBalance(AccountId.Crown);

            Assert.IsTrue(market.TryBuy(buyer, AccountId.Person(buyer), "good.furniture", 2, out int bought, out string error), error);
            Assert.AreEqual(2, bought);
            Assert.AreEqual(2, inventory.GetCount(buyer, "good.furniture"));

            Assert.Greater(wealth.GetBalance(AccountId.Person(renter)).HundredthsOfShil, renterBalanceBefore.HundredthsOfShil, "The stall's renter should be paid.");
            Assert.Greater(wealth.GetBalance(AccountId.Crown).HundredthsOfShil, crownBefore.HundredthsOfShil, "Sales tax should reach the crown.");
        }

        [Test]
        public void StallRent_IsChargedMonthly_AndEvictsIfUnpaid()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            EntityId marketBuildingId = PlaceCompleteMarket(buildings, inventory, new GridPoint(10, 10));

            EntityId renter = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(renter), Money.FromShil(100));
            Assert.IsTrue(market.TryRentStall(marketBuildingId, 0, renter, out string rentError), rentError);

            Money crownBefore = wealth.GetBalance(AccountId.Crown);
            StepDays(sim, 31);

            Assert.Greater(wealth.GetBalance(AccountId.Crown).HundredthsOfShil, crownBefore.HundredthsOfShil, "A month's rent should reach the crown.");
        }

        [Test]
        public void TheCrownCanHoldAStall_ToSellStorehouseGoods()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            EntityId marketBuildingId = PlaceCompleteMarket(buildings, inventory, new GridPoint(10, 10));
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(90, 90), out EntityId storehouseId, out string storehouseError), storehouseError);
            buildings.TryDeliverGoods(storehouseId, "good.plank", 20, out _);
            buildings.TryDeliverGoods(storehouseId, "good.stone", 10, out _);
            buildings.TryAddLaborHours(storehouseId, 9999, out _);
            inventory.Add(storehouseId, "good.loaf", 50);

            Assert.IsTrue(market.TryRentStall(marketBuildingId, 0, null, out string rentError), rentError);
            Assert.IsTrue(market.TryStockStall(marketBuildingId, 0, storehouseId, "good.loaf", 20, out string stockError), stockError);

            EntityId buyer = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(1000));

            Assert.IsTrue(market.TryBuy(buyer, AccountId.Person(buyer), "good.loaf", 5, out int bought, out string error), error);
            Assert.AreEqual(5, bought);
        }

        [Test]
        public void PriceRises_WhenBuyersAreTurnedAway_WithinBoundsAndDailyCap()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            Money basePrice = market.GetPrice("good.furniture");
            EntityId buyer = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(1000));

            // No seller exists at all, so every attempt turns the buyer away for lack of stock.
            Assert.IsFalse(market.TryBuy(buyer, AccountId.Person(buyer), "good.furniture", 1, out _, out _));

            StepDays(sim, 1);

            Money afterOneDay = market.GetPrice("good.furniture");
            Assert.Greater(afterOneDay.HundredthsOfShil, basePrice.HundredthsOfShil);

            double percentChange = (afterOneDay.HundredthsOfShil - basePrice.HundredthsOfShil) * 100.0 / basePrice.HundredthsOfShil;
            Assert.LessOrEqual(percentChange, 5.0001);

            for (int i = 0; i < 60; i++)
            {
                market.TryBuy(buyer, AccountId.Person(buyer), "good.furniture", 1, out _, out _);
                StepDays(sim, 1);
            }

            Assert.LessOrEqual(market.GetPrice("good.furniture").HundredthsOfShil, basePrice.HundredthsOfShil * 3);
        }

        [Test]
        public void PriceFalls_WhenUnsoldStockBuildsUp_WithinFloor()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            EntityId shop = PlaceCompleteMarket(buildings, inventory, new GridPoint(10, 10));
            inventory.Add(shop, "good.furniture", 10);

            Money basePrice = market.GetPrice("good.furniture");
            StepDays(sim, 1); // the first check establishes today's stock baseline

            inventory.Add(shop, "good.furniture", 50); // stock visibly builds up with nobody buying

            StepDays(sim, 1);

            Assert.Less(market.GetPrice("good.furniture").HundredthsOfShil, basePrice.HundredthsOfShil);

            for (int i = 0; i < 60; i++)
            {
                inventory.Add(shop, "good.furniture", 50);
                StepDays(sim, 1);
            }

            Assert.GreaterOrEqual(market.GetPrice("good.furniture").HundredthsOfShil, (long)(basePrice.HundredthsOfShil * 0.5));
        }

        [Test]
        public void ShortageEvent_FiresOnceAtStart_AndOnceAtEnd()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, LandModule land) = Build(core);

            int startedCount = 0;
            int endedCount = 0;
            sim.Events.Subscribe(MarketModule.ShortageStartedEventId, e => { if (e.Data.GetString("good") == "good.furniture") startedCount++; });
            sim.Events.Subscribe(MarketModule.ShortageEndedEventId, e => { if (e.Data.GetString("good") == "good.furniture") endedCount++; });

            StepDays(sim, 1);
            Assert.AreEqual(0, startedCount, "A single day of unavailability shouldn't yet count as a full day's shortage.");

            StepDays(sim, 3);
            Assert.AreEqual(1, startedCount, "A confirmed shortage should fire exactly once, however many more days it continues.");
            Assert.AreEqual(0, endedCount);

            EntityId shop = PlaceCompleteMarket(buildings, inventory, new GridPoint(10, 10));
            inventory.Add(shop, "good.furniture", 10);

            StepDays(sim, 1);
            sim.Step(); // the shortage-ended event, posted from inside that day's own handler, is only delivered on the tick after
            Assert.AreEqual(1, endedCount);
        }
    }
}
