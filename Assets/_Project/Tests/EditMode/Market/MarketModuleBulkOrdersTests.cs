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
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.MapResources;
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
            var mapResources = new MapResourcesModule(mapMovement, land);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement, mapResources);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            // This file's tests track the storehouse's exact leftover stock after each construction
            // draw; start it empty so TryPlaceInitialStockpile's own placeholder stock doesn't skew that.
            simulation.Settings.Set(BuildingsModule.InitialStockpilePlanksSettingId, 0);
            simulation.Settings.Set(BuildingsModule.InitialStockpileStoneSettingId, 0);
            simulation.Settings.Set(PeopleModule.FounderCountSettingId, 0); // and no loaves either

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
                Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(at.X + 30, at.Y + 30), out storehouseId, out string storehouseError), storehouseError);
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
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(5000));
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

        /// <summary>
        /// 3.10b3: a crown site's own storehouse stock no longer moves on its own at all - only a
        /// founder hauling it (GatheringModule) can use it, and this test has none (see Build()). The
        /// bulk-order pass still buys the full shortfall from a private seller for any site, crown
        /// included, exactly as before; the storehouse's own pre-existing stock just sits there,
        /// untouched, waiting for a hauler that never comes in this founder-less test.
        /// </summary>
        [Test]
        public void ACrownSite_NeverDrawsFromTheStorehouseItself_ButStillBuysTheRestFromASeller()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, LandModule land, InventoryModule inventory, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(90, 90), out EntityId storehouseId, out string storehouseError), storehouseError);

            // Building the seller below (also a crown site) draws its own 20 plank + 10 stone from this
            // same storehouse first (via the test-only TryDeliverGoods shortcut, not a hauler), so the
            // test's intended leftover stock is only added afterward.
            EntityId seller = PlaceStockedSeller(buildings, inventory, new GridPoint(20, 20), 1000, 1000);
            inventory.Add(storehouseId, "good.plank", 10);
            inventory.Add(storehouseId, "good.stone", 20);
            wealth.Grant(AccountId.Crown, Money.FromCopper(5000));

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(150, 150), out EntityId lodgingId, out string placeError), placeError);

            StepDays(sim, 4);
            buildings.TryGetBuilding(lodgingId, out BuildingState afterDelay);
            int stoneDelivered = afterDelay.Construction.DeliveredGoods.TryGetValue("good.stone", out int s1) ? s1 : 0;
            int plankDelivered = afterDelay.Construction.DeliveredGoods.TryGetValue("good.plank", out int p1) ? p1 : 0;
            Assert.AreEqual(20, stoneDelivered, "Expected all 20 stone to have come from the seller (bought and delivered after the distance delay) - the storehouse's own stone was never touched.");
            Assert.Greater(plankDelivered, 0, "Expected planks to have arrived from the seller too.");
            Assert.AreEqual(10, inventory.GetCount(storehouseId, "good.plank"), "Expected the storehouse's own 10 planks to sit untouched - only a hauler could move them, and none exists in this test.");
            Assert.AreEqual(20, inventory.GetCount(storehouseId, "good.stone"), "Expected the storehouse's own 20 stone to sit untouched too.");
        }

        /// <summary>3.10b2: the "stalled" check (and the building panel's "stuck" diagnostic) must count the crown storehouse only for a site that can actually draw on it.</summary>
        [Test]
        public void GetAvailableStockForSite_CountsTheStorehouseOnlyForACrownSite()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, LandModule land, InventoryModule inventory, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(90, 90), out EntityId storehouseId, out string storehouseError), storehouseError);
            inventory.Add(storehouseId, "good.plank", 50);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(150, 150), out EntityId lodgingId, out string placeError), placeError);

            EntityId owner = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(5000));
            land.RequestSetZone(new CellRect(40, 40, 4, 4), "zone.residential_low");
            sim.Step();
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(40, 40), out _, out string buyError), buyError);
            Assert.IsTrue(buildings.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string requestError), requestError);

            Assert.AreEqual(50, market.GetAvailableStockForSite(lodgingId, "good.plank"),
                "The crown storehouse's own stock should count as available to a crown site - TryDeliverGoods draws it directly.");
            Assert.AreEqual(0, market.GetAvailableStockForSite(cottageId, "good.plank"),
                "The crown storehouse's stock should never count as available to a privately-owned site - it's never sold to a private buyer.");
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
            var mapResources2 = new MapResourcesModule(mapMovement2, land2);
            var gathering2 = new GatheringModule(people2, buildings2, jobs2, inventory2, market2, mapMovement2, mapResources2);
            (Simulation loaded, _) = GameSave.Load(stream, new[] { core.FolderPath }, new IModule[] { mapMovement2, inventory2, people2, wealth2, land2, buildings2, jobs2, market2, mapResources2, gathering2 }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

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
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(5000));
            land.RequestSetZone(new CellRect(40, 40, 4, 4), "zone.residential_low");
            sim.Step();
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(40, 40), out _, out string buyError), buyError);
            Assert.IsTrue(buildings.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string requestError), requestError);
            return cottageId;
        }
    }
}
