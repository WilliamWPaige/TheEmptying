using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
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

namespace TheEmptying.Tests.EditMode.Inventory
{
    public class InventoryModuleTests
    {
        private static (Simulation Simulation, InventoryModule Module) Build(TempCoreContentCopy core)
        {
            var module = new InventoryModule();
            var mapMovement = new MapMovementModule();
            var people = new PeopleModule(mapMovement, module);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, module, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, module, people, mapMovement);
            var mapResources = new MapResourcesModule(mapMovement, land);
            var gathering = new GatheringModule(people, buildings, jobs, module, market, mapMovement, mapResources);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { module, mapMovement, people, wealth, land, buildings, jobs, market, mapResources, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, module);
        }

        [Test]
        public void Add_ThenGetCount_ReflectsTheTotal()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, InventoryModule module) = Build(core);
            EntityId entity = sim.Entities.Create(EntityKind.Item);

            module.Add(entity, "good.loaf", 5);
            module.Add(entity, "good.loaf", 3);

            Assert.AreEqual(8, module.GetCount(entity, "good.loaf"));
        }

        [Test]
        public void TryRemove_WithEnough_Succeeds()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, InventoryModule module) = Build(core);
            EntityId entity = sim.Entities.Create(EntityKind.Item);
            module.Add(entity, "good.loaf", 5);

            bool result = module.TryRemove(entity, "good.loaf", 3);

            Assert.IsTrue(result);
            Assert.AreEqual(2, module.GetCount(entity, "good.loaf"));
        }

        [Test]
        public void TryRemove_MoreThanAvailable_FailsAndLeavesCountUnchanged()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, InventoryModule module) = Build(core);
            EntityId entity = sim.Entities.Create(EntityKind.Item);
            module.Add(entity, "good.loaf", 2);

            bool result = module.TryRemove(entity, "good.loaf", 3);

            Assert.IsFalse(result);
            Assert.AreEqual(2, module.GetCount(entity, "good.loaf"));
        }

        [Test]
        public void TryRemove_FromAnEntityWithNoInventory_Fails()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, InventoryModule module) = Build(core);
            EntityId entity = sim.Entities.Create(EntityKind.Item);

            Assert.IsFalse(module.TryRemove(entity, "good.loaf", 1));
        }

        [Test]
        public void Count_NeverGoesNegative()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, InventoryModule module) = Build(core);
            EntityId entity = sim.Entities.Create(EntityKind.Item);
            module.Add(entity, "good.loaf", 1);

            module.TryRemove(entity, "good.loaf", 1);
            Assert.AreEqual(0, module.GetCount(entity, "good.loaf"));

            // Removing again (nothing left) must fail, not go negative.
            Assert.IsFalse(module.TryRemove(entity, "good.loaf", 1));
            Assert.AreEqual(0, module.GetCount(entity, "good.loaf"));
        }

        [Test]
        public void GetAll_ListsEveryGoodInOrdinalIdOrder()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, InventoryModule module) = Build(core);
            EntityId entity = sim.Entities.Create(EntityKind.Item);
            module.Add(entity, "good.wheat", 1);
            module.Add(entity, "good.loaf", 2);
            module.Add(entity, "good.block", 3);

            List<string> ids = module.GetAll(entity).Select(kvp => kvp.Key).ToList();

            CollectionAssert.AreEqual(new[] { "good.block", "good.loaf", "good.wheat" }, ids);
        }

        [Test]
        public void SaveAndLoad_PreservesEveryInventory()
        {
            using var core = new TempCoreContentCopy();
            var firstModule = new InventoryModule();
            var firstMapMovement = new MapMovementModule();
            var firstPeople = new PeopleModule(firstMapMovement, firstModule);
            var firstWealth = new WealthModule(firstPeople);
            var firstLand = new LandModule(firstMapMovement, firstWealth);
            var firstBuildings = new BuildingsModule(firstMapMovement, firstLand, firstWealth, firstModule, firstPeople);
            var firstJobs = new JobsModule(firstBuildings, firstWealth, firstPeople, firstMapMovement);
            var firstMarket = new MarketModule(firstBuildings, firstWealth, firstModule, firstPeople, firstMapMovement);
            var firstMapResources = new MapResourcesModule(firstMapMovement, firstLand);
            var firstGathering = new GatheringModule(firstPeople, firstBuildings, firstJobs, firstModule, firstMarket, firstMapMovement, firstMapResources);
            (Simulation firstSim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { firstModule, firstMapMovement, firstPeople, firstWealth, firstLand, firstBuildings, firstJobs, firstMarket, firstMapResources, firstGathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            EntityId a = firstSim.Entities.Create(EntityKind.Item);
            EntityId b = firstSim.Entities.Create(EntityKind.Item);
            firstModule.Add(a, "good.loaf", 10);
            firstModule.Add(b, "good.plank", 4);
            firstModule.Add(b, "good.stone", 2);

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var secondModule = new InventoryModule();
            var secondMapMovement = new MapMovementModule();
            var secondPeople = new PeopleModule(secondMapMovement, secondModule);
            var secondWealth = new WealthModule(secondPeople);
            var secondLand = new LandModule(secondMapMovement, secondWealth);
            var secondBuildings = new BuildingsModule(secondMapMovement, secondLand, secondWealth, secondModule, secondPeople);
            var secondJobs = new JobsModule(secondBuildings, secondWealth, secondPeople, secondMapMovement);
            var secondMarket = new MarketModule(secondBuildings, secondWealth, secondModule, secondPeople, secondMapMovement);
            var secondMapResources = new MapResourcesModule(secondMapMovement, secondLand);
            var secondGathering = new GatheringModule(secondPeople, secondBuildings, secondJobs, secondModule, secondMarket, secondMapMovement, secondMapResources);
            (Simulation secondSim, _) = GameSave.Load(stream, new[] { core.FolderPath }, new IModule[] { secondModule, secondMapMovement, secondPeople, secondWealth, secondLand, secondBuildings, secondJobs, secondMarket, secondMapResources, secondGathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.AreEqual(10, secondModule.GetCount(a, "good.loaf"));
            Assert.AreEqual(4, secondModule.GetCount(b, "good.plank"));
            Assert.AreEqual(2, secondModule.GetCount(b, "good.stone"));
        }
    }
}
