using System.IO;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Buildings
{
    public class BuildingsModuleOccupancyTests
    {
        private static IModule[] Modules(out BuildingsModule buildings, out MapMovementModule mapMovement)
        {
            mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering };
        }

        [Test]
        public void ABuildingsFootprint_BlocksEveryCellItCovers()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = Modules(out BuildingsModule buildings, out MapMovementModule mapMovement);
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            var at = new GridPoint(10, 10);
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", at, out EntityId id, out string error), error);

            bool found = false;
            foreach (var row in buildings.Buildings)
            {
                if (row.Key != id)
                {
                    continue;
                }

                found = true;
                foreach (GridPoint cell in row.Value.Rect.Cells())
                {
                    Assert.IsTrue(mapMovement.Grid.IsOccupied(cell));
                }
            }

            Assert.IsTrue(found);
        }

        [Test]
        public void Occupancy_IsRebuiltIdenticallyAfterLoad()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 11;

            IModule[] firstModules = Modules(out BuildingsModule firstBuildings, out MapMovementModule firstMapMovement);
            (Simulation firstSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, firstModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            firstBuildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(10, 10), out _, out _);

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] secondModules = Modules(out BuildingsModule _, out MapMovementModule secondMapMovement);
            (Simulation secondSim, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (int y = 0; y < firstMapMovement.Grid.Depth; y++)
            {
                for (int x = 0; x < firstMapMovement.Grid.Width; x++)
                {
                    var cell = new GridPoint(x, y);
                    Assert.AreEqual(firstMapMovement.Grid.IsOccupied(cell), secondMapMovement.Grid.IsOccupied(cell), $"Occupancy mismatch at {cell}.");
                }
            }
        }
    }
}
