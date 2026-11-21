using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>3.9h: a new game starts at noon, not midnight, so founders are awake and working from the start.</summary>
    public class NewGameStartTests
    {
        private static (Simulation Simulation, GatheringModule Gathering, BuildingsModule Buildings, PeopleModule People) Build(TempCoreContentCopy core)
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
            return (simulation, gathering, buildings, people);
        }

        [Test]
        public void NewGame_ClockReadsNoonOnDay1()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, _, _) = Build(core);

            GameDate start = GameDate.FromTick(sim.Clock.CurrentTick);
            Assert.AreEqual(1, start.Year);
            Assert.AreEqual(1, start.DayOfMonth);
            Assert.AreEqual(12, start.Hour);
            Assert.AreEqual(0, start.Minute);
        }

        [Test]
        public void NewGame_FoundersAreWorking_WithinTheFirstHour_AfterTheStockpileIsPlaced()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, GatheringModule gathering, BuildingsModule buildings, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            for (long i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            int workingFounders = people.People.Count(row => people.IsFounder(row.Key) && gathering.GetTaskKind(row.Key) != GatherTaskKind.None);
            Assert.Greater(workingFounders, 0, "Expected at least one founder already gathering or building within the first in-game hour after the stockpile was placed.");
        }
    }
}
