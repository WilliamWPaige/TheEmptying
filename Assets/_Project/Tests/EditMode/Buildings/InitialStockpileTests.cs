using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Buildings
{
    /// <summary>3.9b: the player's first action - placing the stockpile - is free, instant, and seeded with the starting stock. A later one costs materials normally.</summary>
    public class InitialStockpileTests
    {
        private static (Simulation Simulation, BuildingsModule Buildings, InventoryModule Inventory, PeopleModule People) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, buildings, inventory, people);
        }

        [Test]
        public void NewGame_NeedsTheStockpilePlaced_UntilThePlayerDoesSo()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, _, _) = Build(core);

            Assert.IsTrue(buildings.NeedsInitialStockpilePlacement);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId id, out string error), error);
            Assert.IsFalse(buildings.NeedsInitialStockpilePlacement);
        }

        [Test]
        public void PlacingTheStockpile_IsFreeAndInstant_AndHoldsTheStartingStock()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, InventoryModule inventory, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId id, out string error), error);

            Assert.IsTrue(buildings.TryGetBuilding(id, out BuildingState building));
            Assert.IsNull(building.Construction, "The stockpile should be complete the instant it's placed.");

            int founderCount = (int)sim.Settings.Get(PeopleModule.FounderCountSettingId);
            int loavesPerPerson = (int)sim.Settings.Get(PeopleModule.StartingProvisionsLoavesSettingId);
            int planks = (int)sim.Settings.Get(BuildingsModule.InitialStockpilePlanksSettingId);
            int stone = (int)sim.Settings.Get(BuildingsModule.InitialStockpileStoneSettingId);

            Assert.AreEqual(planks, inventory.GetCount(id, "good.plank"));
            Assert.AreEqual(stone, inventory.GetCount(id, "good.stone"));
            Assert.AreEqual(loavesPerPerson * (founderCount + 1), inventory.GetCount(id, "good.loaf"),
                "Starting bread is counted per person, including the monarch (the +1).");
        }

        [Test]
        public void OnlyTheFirstStockpile_IsFree_ALaterOneCostsMaterialsNormally()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, InventoryModule inventory, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId first, out string error), error);
            Assert.IsFalse(buildings.TryPlaceInitialStockpile(new GridPoint(50, 50), out _, out string secondError), "A second stockpile should never be free.");
            Assert.IsNotNull(secondError);

            // The normal government path still works, but unlike the first stockpile it starts as an
            // ordinary construction site - nothing about it is free.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(50, 50), out EntityId second, out string placeError), placeError);
            Assert.IsTrue(buildings.TryGetBuilding(second, out BuildingState site));
            Assert.IsNotNull(site.Construction, "A normally-placed storehouse should start as a construction site, not complete.");

            // Its materials draw from the first (already complete) storehouse, exactly like any other crown site.
            Assert.IsTrue(buildings.TryDeliverGoods(second, "good.plank", 20, out string deliverError), deliverError);
            int planksLeftInFirst = inventory.GetCount(first, "good.plank");
            int startingPlanks = (int)sim.Settings.Get(BuildingsModule.InitialStockpilePlanksSettingId);
            Assert.AreEqual(startingPlanks - 20, planksLeftInFirst, "The second storehouse's materials should come out of the first.");
        }

        [Test]
        public void FoundersEatFree_FromTheStockpile_NotTheirOwnInventory()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, InventoryModule inventory, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            foreach (var row in people.People)
            {
                Assert.AreEqual(0, inventory.GetCount(row.Key, "good.loaf"), "A founder or the monarch should carry no food of their own.");
            }

            int loavesBefore = inventory.GetCount(storehouseId, "good.loaf");
            Assert.Greater(loavesBefore, 0);
        }
    }
}
