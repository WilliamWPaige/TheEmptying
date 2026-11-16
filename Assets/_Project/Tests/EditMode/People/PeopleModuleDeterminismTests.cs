using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.People
{
    public class PeopleModuleDeterminismTests
    {
        private static (Simulation Simulation, PeopleModule People) Build(TempCoreContentCopy core, ulong seed)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, people);
        }

        private static IModule[] Modules(out PeopleModule people, InventoryModule inventory, MapMovementModule mapMovement)
        {
            people = new PeopleModule(mapMovement, inventory);
            return new IModule[] { mapMovement, inventory, people, new WealthModule(people) };
        }

        private static List<string> Names(PeopleModule module) =>
            module.People.Select(row => row.Value.FullName).OrderBy(n => n, System.StringComparer.Ordinal).ToList();

        private static List<double> Ages(PeopleModule module) =>
            module.People.Select(row => row.Value.AgeLifeYears).OrderBy(a => a).ToList();

        [Test]
        public void SameSeed_ThreeMonths_ProducesIdenticalNamesAgesAndStateHash()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();

            (Simulation simA, PeopleModule peopleA) = Build(coreA, 12345);
            (Simulation simB, PeopleModule peopleB) = Build(coreB, 12345);

            for (long i = 0; i < CalendarConstants.TicksPerMonth * 3; i++)
            {
                simA.Step();
                simB.Step();
            }

            CollectionAssert.AreEqual(Names(peopleA), Names(peopleB));
            CollectionAssert.AreEqual(Ages(peopleA), Ages(peopleB));
            Assert.AreEqual(simA.ComputeStateHash(), simB.ComputeStateHash());
        }

        [Test]
        public void SaveAndLoad_MidMonth_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 999;
            long n = CalendarConstants.TicksPerMonth / 2 + 123; // before the save, mid-month
            long m = CalendarConstants.TicksPerMonth / 2 + 500; // after the load, crossing into the next month

            var uninterruptedMap = new MapMovementModule();
            var uninterruptedInv = new InventoryModule();
            IModule[] uninterruptedModules = Modules(out PeopleModule _, uninterruptedInv, uninterruptedMap);
            (Simulation uninterruptedSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, uninterruptedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (long i = 0; i < n + m; i++)
            {
                uninterruptedSim.Step();
            }

            ulong uninterruptedHash = uninterruptedSim.ComputeStateHash();

            var firstMap = new MapMovementModule();
            var firstInv = new InventoryModule();
            IModule[] firstModules = Modules(out PeopleModule _, firstInv, firstMap);
            (Simulation firstSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, firstModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (long i = 0; i < n; i++)
            {
                firstSim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 8 }, stream);
            stream.Position = 0;

            var secondMap = new MapMovementModule();
            var secondInv = new InventoryModule();
            IModule[] secondModules = Modules(out PeopleModule _, secondInv, secondMap);
            (Simulation secondSim, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (long i = 0; i < m; i++)
            {
                secondSim.Step();
            }

            Assert.AreEqual(uninterruptedHash, secondSim.ComputeStateHash());
        }
    }
}
