using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
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
    /// <summary>Bulk orders: construction sites source materials from the nearest seller, delivered after a distance-based delay; a crown site draws its own storehouse first.</summary>
    public class MarketModuleBulkOrdersTests
    {
        private static (Simulation Simulation, MarketModule Market, BuildingsModule Buildings, WealthModule Wealth, LandModule Land, InventoryModule Inventory, PeopleModule People) Build(TempCoreContentCopy core)
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

            return (simulation, market, buildings, wealth, land, inventory, people);
        }

        private static void StepDays(Simulation sim, int days)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay * days; i++)
            {
                sim.Step();
            }
        }

        /// <summary>
        /// A seller building, stocked directly (standing in for a shop with no production chain yet).
        /// The market building is itself a crown site, so finishing it also draws 20 plank + 10 stone
        /// from the crown storehouse (bootstrapping one, stocked with just enough, if none exists yet) -
        /// a caller that cares about the storehouse's exact leftover stock should add its own test
        /// amounts only after this call returns.
        /// </summary>
        private static EntityId PlaceStockedSeller(BuildingsModule buildings, InventoryModule inventory, GridPoint at, int plank, int stone)
        {
            if (!buildings.TryFindCrownStorehouse(out EntityId storehouseId))
            {
                Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(at.X + 30, at.Y + 30), out storehouseId, out string storehouseError), storehouseError);
                buildings.TryDeliverGoods(storehouseId, "good.plank", 20, out _); // the storehouse's own construction is free (nothing else can supply the first one)
                buildings.TryDeliverGoods(storehouseId, "good.stone", 10, out _);
                buildings.TryAddLaborHours(storehouseId, 9999, out _);
            }

            inventory.Add(storehouseId, "good.plank", 20); // exactly the market's own construction need
            inventory.Add(storehouseId, "good.stone", 10);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.market", at, out EntityId sellerId, out string sellerError), sellerError);
            Assert.IsTrue(buildings.TryDeliverGoods(sellerId, "good.plank", 20, out string plankError), plankError);
            Assert.IsTrue(buildings.TryDeliverGoods(sellerId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildings.TryAddLaborHours(sellerId, 9999, out string laborError), laborError);

            inventory.Add(sellerId, "good.plank", plank);
            inventory.Add(sellerId, "good.stone", stone);
            return sellerId;
        }

        [Test]
        public void APrivateSite_BuysFromTheNearestSeller_AfterTheDistanceDelay_AndCompletes()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, LandModule land, InventoryModule inventory, PeopleModule people) = Build(core);

            EntityId seller = PlaceStockedSeller(buildings, inventory, new GridPoint(20, 20), 1000, 1000);

            EntityId owner = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(owner), Money.FromShil(5000));
            land.RequestSetZone(new CellRect(40, 40, 4, 4), "zone.residential_low");
            sim.Step();
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(40, 40), out _, out string buyError), buyError);
            Assert.IsTrue(buildings.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string requestError), requestError);

            // The owner has no plank or stone of their own: only Market's bulk orders can supply this site.
            StepDays(sim, 1);

            buildings.TryGetBuilding(cottageId, out BuildingState siteAfterOneDay);
            Assert.IsNotNull(siteAfterOneDay.Construction, "The order was only just placed; it shouldn't have arrived within the same tick.");

            StepDays(sim, 2);

            buildings.TryGetBuilding(cottageId, out BuildingState siteAfterDelay);
            int plankDelivered = siteAfterDelay.Construction?.DeliveredGoods.TryGetValue("good.plank", out int p) == true ? p : 40;
            int stoneDelivered = siteAfterDelay.Construction?.DeliveredGoods.TryGetValue("good.stone", out int s) == true ? s : 10;
            Assert.Greater(plankDelivered, 0, "The bought planks should have arrived by now.");
            Assert.Greater(stoneDelivered, 0, "The bought stone should have arrived by now.");

            StepDays(sim, 5);
            buildings.TryAddLaborHours(cottageId, 50, out _);
            buildings.TryGetBuilding(cottageId, out BuildingState siteFinal);
            Assert.IsNull(siteFinal.Construction, "With materials bought and labor supplied, the cottage should complete.");
        }

        [Test]
        public void ACrownSite_DrawsFromTheStorehouseFirst_ThenBuysTheRest()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, LandModule land, InventoryModule inventory, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(90, 90), out EntityId storehouseId, out string storehouseError), storehouseError);
            buildings.TryDeliverGoods(storehouseId, "good.plank", 20, out _); // the storehouse's own construction is free (nothing else can supply the first one)
            buildings.TryDeliverGoods(storehouseId, "good.stone", 10, out _);
            buildings.TryAddLaborHours(storehouseId, 9999, out _);

            // Building the seller below (also a crown site) draws its own 20 plank + 10 stone from this
            // same storehouse first, so the test's intended leftover stock is only added afterward.
            EntityId seller = PlaceStockedSeller(buildings, inventory, new GridPoint(20, 20), 1000, 1000);
            inventory.Add(storehouseId, "good.plank", 10); // only enough for a sixth of the lodging's plank need
            inventory.Add(storehouseId, "good.stone", 20); // enough for all of its stone need
            wealth.Grant(AccountId.Crown, Money.FromShil(5000));

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(150, 150), out EntityId lodgingId, out string placeError), placeError);

            StepDays(sim, 1);
            buildings.TryGetBuilding(lodgingId, out BuildingState afterOrder);
            int stoneSoFar = afterOrder.Construction.DeliveredGoods.TryGetValue("good.stone", out int s0) ? s0 : 0;
            Assert.AreEqual(20, stoneSoFar, "All the stone the storehouse had should be drawn immediately, with no transit delay.");

            StepDays(sim, 3);
            buildings.TryGetBuilding(lodgingId, out BuildingState afterDelay);
            int plankSoFar = afterDelay.Construction.DeliveredGoods.TryGetValue("good.plank", out int p0) ? p0 : 0;
            Assert.Greater(plankSoFar, 10, "The storehouse's 10 planks plus the bought remainder should both be credited by now.");
        }

        [Test]
        public void ACrownSite_WithNoGoodsAndNoMoney_Stalls()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, LandModule land, InventoryModule inventory, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(150, 150), out EntityId lodgingId, out string placeError), placeError);

            StepDays(sim, 10);

            buildings.TryGetBuilding(lodgingId, out BuildingState site);
            Assert.IsNotNull(site.Construction, "With no storehouse, no seller and no crown money, the site should simply stall.");
            Assert.AreEqual(0, site.Construction.DeliveredGoods.Count == 0 ? 0 : site.Construction.DeliveredGoods.Values.Sum());
        }

        [Test]
        public void SaveAndLoad_MidDelivery_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();

            (Simulation uninterrupted, MarketModule uMarket, BuildingsModule uBuildings, WealthModule uWealth, LandModule uLand, InventoryModule uInventory, PeopleModule uPeople) = Build(core);
            SetUpMidDeliveryScenario(uninterrupted, uBuildings, uWealth, uLand, uInventory, uPeople);
            StepDays(uninterrupted, 1); // the order is placed but not yet delivered

            (Simulation first, MarketModule fMarket, BuildingsModule fBuildings, WealthModule fWealth, LandModule fLand, InventoryModule fInventory, PeopleModule fPeople) = Build(core);
            EntityId cottageIdFirst = SetUpMidDeliveryScenario(first, fBuildings, fWealth, fLand, fInventory, fPeople);
            StepDays(first, 1);

            using var stream = new MemoryStream();
            GameSave.Save(first, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var mapMovement2 = new MapMovementModule();
            var inventory2 = new InventoryModule();
            var people2 = new PeopleModule(mapMovement2, inventory2);
            var wealth2 = new WealthModule(people2);
            var land2 = new LandModule(mapMovement2, wealth2);
            var buildings2 = new BuildingsModule(mapMovement2, land2, wealth2, inventory2, people2);
            var jobs2 = new JobsModule(buildings2, wealth2, people2, mapMovement2);
            var market2 = new MarketModule(buildings2, wealth2, inventory2, people2, mapMovement2);
            (Simulation loaded, _) = GameSave.Load(stream, new[] { core.FolderPath }, new IModule[] { mapMovement2, inventory2, people2, wealth2, land2, buildings2, jobs2, market2 }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            StepDays(uninterrupted, 3);
            StepDays(loaded, 3);

            buildings2.TryGetBuilding(cottageIdFirst, out BuildingState loadedSite);
            uBuildings.TryGetBuilding(cottageIdFirst, out BuildingState uninterruptedSite);

            bool loadedComplete = loadedSite.Construction == null;
            bool uninterruptedComplete = uninterruptedSite.Construction == null;
            Assert.AreEqual(uninterruptedComplete, loadedComplete, "Saving and loading mid-delivery should reach the same outcome as an unbroken run.");

            if (!loadedComplete)
            {
                int loadedPlank = loadedSite.Construction.DeliveredGoods.TryGetValue("good.plank", out int lp) ? lp : 0;
                int uninterruptedPlank = uninterruptedSite.Construction.DeliveredGoods.TryGetValue("good.plank", out int up) ? up : 0;
                Assert.AreEqual(uninterruptedPlank, loadedPlank);
            }
        }

        private static EntityId SetUpMidDeliveryScenario(Simulation sim, BuildingsModule buildings, WealthModule wealth, LandModule land, InventoryModule inventory, PeopleModule people)
        {
            PlaceStockedSeller(buildings, inventory, new GridPoint(20, 20), 1000, 1000);

            EntityId owner = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(owner), Money.FromShil(5000));
            land.RequestSetZone(new CellRect(40, 40, 4, 4), "zone.residential_low");
            sim.Step();
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(40, 40), out _, out string buyError), buyError);
            Assert.IsTrue(buildings.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string requestError), requestError);
            return cottageId;
        }
    }
}
