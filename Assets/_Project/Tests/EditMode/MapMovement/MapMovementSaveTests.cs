using System.IO;
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
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.MapMovement
{
    public class MapMovementSaveTests
    {
        private static void SpawnAndSendMovers(MapMovementModule module)
        {
            for (int i = 0; i < 10; i++)
            {
                EntityId mover = module.SpawnMover();
                module.TryMoveTo(mover, new GridPoint((i * 7) % 200 + 10, (i * 13) % 200 + 10));
            }
        }

        private static IModule[] Modules(MapMovementModule mapMovement)
        {
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering };
        }

        [Test]
        public void InterruptedRun_SaveAndLoadMidWalk_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 777;
            const int n = 40; // before the save, movers mid-walk and mid-search
            const int m = 35; // after the load

            var uninterruptedModule = new MapMovementModule();
            (Simulation uninterruptedSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, Modules(uninterruptedModule), contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            SpawnAndSendMovers(uninterruptedModule);

            for (int i = 0; i < n + m; i++)
            {
                uninterruptedSim.Step();
            }

            ulong uninterruptedHash = uninterruptedSim.ComputeStateHash();

            var firstModule = new MapMovementModule();
            (Simulation firstSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, Modules(firstModule), contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            SpawnAndSendMovers(firstModule);

            for (int i = 0; i < n; i++)
            {
                firstSim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var secondModule = new MapMovementModule();
            (Simulation secondSim, _) = GameSave.Load(stream, new[] { core.FolderPath }, Modules(secondModule), contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (int i = 0; i < m; i++)
            {
                secondSim.Step();
            }

            Assert.AreEqual(uninterruptedHash, secondSim.ComputeStateHash());
        }
    }
}
