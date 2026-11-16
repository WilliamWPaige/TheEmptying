using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Buildings
{
    public class BuildingsModuleConstructionTests
    {
        private static (Simulation Simulation, BuildingsModule Buildings, LandModule Land, WealthModule Wealth, InventoryModule Inventory) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, buildings, land, wealth, inventory);
        }

        private static EntityId PlaceCrownLodgingSite(BuildingsModule buildingsModule)
        {
            Assert.IsTrue(buildingsModule.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(10, 10), out EntityId id, out string error), error);
            return id;
        }

        [Test]
        public void GoodsAndLaborAccumulate_TowardCompletion()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, _, _, InventoryModule inventory) = Build(core);
            EntityId siteId = PlaceCrownLodgingSite(buildingsModule);

            int completedCount = 0;
            sim.Events.Subscribe(BuildingsModule.BuildingCompletedEventId, e => { if (e.Data.GetEntity("building") == siteId) completedCount++; });

            Assert.IsTrue(buildingsModule.TryDeliverGoods(siteId, "good.plank", 30, out string error1), error1);
            sim.Step(); // BuildingCompletedEventId (if any) is only delivered on the next tick
            Assert.AreEqual(0, completedCount, "Half the planks and none of the stone should not complete it.");

            Assert.IsTrue(buildingsModule.TryDeliverGoods(siteId, "good.plank", 30, out string error2), error2);
            Assert.IsTrue(buildingsModule.TryDeliverGoods(siteId, "good.stone", 20, out string error3), error3);
            sim.Step();
            Assert.AreEqual(0, completedCount, "All goods delivered but no labor yet should not complete it.");

            Assert.IsTrue(buildingsModule.TryAddLaborHours(siteId, 80, out string error4), error4);
            sim.Step();
            Assert.AreEqual(1, completedCount, "60 planks + 20 stone = 80 labor hours at the default 1 hour per good unit.");
        }

        [Test]
        public void Completion_FiresExactlyOnce()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, _, _, InventoryModule inventory) = Build(core);
            EntityId siteId = PlaceCrownLodgingSite(buildingsModule);

            int completedCount = 0;
            sim.Events.Subscribe(BuildingsModule.BuildingCompletedEventId, e => { if (e.Data.GetEntity("building") == siteId) completedCount++; });

            buildingsModule.TryDeliverGoods(siteId, "good.plank", 60, out _);
            buildingsModule.TryDeliverGoods(siteId, "good.stone", 20, out _);
            buildingsModule.TryAddLaborHours(siteId, 80, out _);
            sim.Step();
            Assert.AreEqual(1, completedCount);

            // Further calls on an already-complete site should be refused cleanly, not re-fire completion.
            Assert.IsFalse(buildingsModule.TryAddLaborHours(siteId, 1, out string error));
            Assert.IsNotNull(error);
            sim.Step();
            Assert.AreEqual(1, completedCount);
        }

        [Test]
        public void ASite_StallsWithoutSupplies_AndResumesLater()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, _, _, InventoryModule inventory) = Build(core);
            EntityId siteId = PlaceCrownLodgingSite(buildingsModule);

            buildingsModule.TryDeliverGoods(siteId, "good.plank", 60, out _);
            buildingsModule.TryAddLaborHours(siteId, 40, out _); // half the labor; still missing all the stone

            int completedCount = 0;
            sim.Events.Subscribe(BuildingsModule.BuildingCompletedEventId, e => { if (e.Data.GetEntity("building") == siteId) completedCount++; });

            for (long i = 0; i < 1000; i++)
            {
                sim.Step(); // time passing alone never finishes a stalled site
            }

            Assert.AreEqual(0, completedCount);

            Assert.IsTrue(buildingsModule.TryDeliverGoods(siteId, "good.stone", 20, out string error1), error1);
            Assert.IsTrue(buildingsModule.TryAddLaborHours(siteId, 40, out string error2), error2);
            sim.Step();
            Assert.AreEqual(1, completedCount, "Supplying the rest should let it resume and finish.");
        }

        [Test]
        public void PrivateSite_DrawsDeliveredGoodsFromTheOwnersInventory()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromShil(5000));
            land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out _, out _);
            buildingsModule.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId siteId, out string requestError);
            Assert.IsNull(requestError);

            Assert.IsFalse(buildingsModule.TryDeliverGoods(siteId, "good.plank", 10, out string error), "The owner has no planks yet.");
            Assert.IsNotNull(error);

            inventory.Add(owner, "good.plank", 40);
            Assert.IsTrue(buildingsModule.TryDeliverGoods(siteId, "good.plank", 10, out string error2), error2);
            Assert.AreEqual(30, inventory.GetCount(owner, "good.plank"));
        }
    }
}
