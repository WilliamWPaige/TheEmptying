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
using TheEmptying.Modules.MapResources;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Jobs
{
    public class JobsModuleConstructionTests
    {
        private static (Simulation Simulation, JobsModule Jobs, BuildingsModule Buildings, PeopleModule People, InventoryModule Inventory) Build(TempCoreContentCopy core)
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

            foreach (var row in people.People.ToList())
            {
                simulation.Entities.Destroy(row.Key);
            }

            // 3.10b: this file spawns ad-hoc test people with no home and no concept of immigration
            // yet - without this, the founders' own "someone is homeless" trigger would build an
            // unrelated log cabin, competing for the one person these tests actually care about.
            simulation.Settings.Set(GatheringModule.LogCabinEnabledSettingId, 0);

            return (simulation, jobs, buildings, people, inventory);
        }

        private static void StepDays(Simulation sim, int days)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay * days; i++)
            {
                sim.Step();
            }
        }

        [Test]
        public void HiredBuilders_AddLaborAtTheirWorkSpeed_UntilTheSiteFinishes()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, PeopleModule people, InventoryModule inventory) = Build(core);

            // A second storehouse needs a first, already-complete one to draw its own construction materials from.
            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(50, 50), out EntityId fundingStorehouseId, out string fundError), fundError);
            inventory.Add(fundingStorehouseId, "good.plank", 1000);
            inventory.Add(fundingStorehouseId, "good.stone", 1000);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(10, 10), out EntityId siteId, out string error), error);
            buildings.TryDeliverGoods(siteId, "good.plank", 20, out _);
            buildings.TryDeliverGoods(siteId, "good.stone", 10, out _); // goods done; no labor yet

            StepDays(sim, 1); // creates the builder opening

            Assert.IsTrue(jobs.TryGetJobs(siteId, out JobsState state));
            Assert.IsNotNull(state.Builder);
            Assert.IsFalse(state.Builder.Occupants[0].HasValue);

            EntityId builder = people.SpawnPerson(30, 30);
            inventory.Add(builder, "good.loaf", 10000);

            int completedCount = 0;
            sim.Events.Subscribe(BuildingsModule.BuildingCompletedEventId, e =>
            {
                if (e.Data.GetEntity("building") == siteId)
                {
                    completedCount++;
                }
            });

            StepDays(sim, 1); // hired as builder
            Assert.IsTrue(jobs.TryGetJobs(siteId, out state));
            Assert.AreEqual(builder, state.Builder.Occupants[0]);

            StepDays(sim, 20); // plenty of time to work off 30 labor hours at any skill level

            Assert.AreEqual(1, completedCount, "The site should finish once goods and labor are both complete.");
            Assert.IsTrue(jobs.TryGetJobs(siteId, out JobsState finishedState));
            Assert.IsNull(finishedState.Builder, "The temporary builder slot should be cleared once the site is finished.");
        }
    }
}
