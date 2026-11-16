using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Jobs
{
    public class JobsModuleLeavingTests
    {
        private static (Simulation Simulation, JobsModule Jobs, BuildingsModule Buildings, LandModule Land, WealthModule Wealth, PeopleModule People, InventoryModule Inventory) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            foreach (var row in people.People.ToList())
            {
                simulation.Entities.Destroy(row.Key);
            }

            return (simulation, jobs, buildings, land, wealth, people, inventory);
        }

        private static void StepDays(Simulation sim, int days)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay * days; i++)
            {
                sim.Step();
            }
        }

        private static EntityId DiscoverAndBuildBakery(Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, InventoryModule inventory, EntityId owner, GridPoint plotAt)
        {
            land.RequestSetZone(new CellRect(plotAt.X, plotAt.Y, 6, 6), "zone.commercial");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromShil(10000));
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.commercial", plotAt, out _, out string buyError), buyError);

            EntityId bakeryId;
            if (!buildings.TryRequestPrivateBuilding(owner, "building.bakery", out bakeryId, out _))
            {
                // Undiscovered: answering "Allow" makes BuildingsModule retry the original request internally,
                // so we must not call TryRequestPrivateBuilding again - the plot would already have a building on it.
                long questionId = sim.RuleRunner.PendingQuestions[0].QuestionId;
                sim.AnswerQuestion(questionId, 1); // Allow
                sim.Step();

                bakeryId = EntityId.None;
                foreach (var row in buildings.Buildings)
                {
                    if (row.Value.Owner == owner && row.Value.BuildingTypeId == "building.bakery")
                    {
                        bakeryId = row.Key;
                        break;
                    }
                }

                Assert.AreNotEqual(EntityId.None, bakeryId, "Answering .Allow. should have let BuildingsModule.s internal retry build the bakery.");
            }

            inventory.Add(owner, "good.plank", 20);
            inventory.Add(owner, "good.stone", 10);
            Assert.IsTrue(buildings.TryDeliverGoods(bakeryId, "good.plank", 20, out string plankError), plankError);
            Assert.IsTrue(buildings.TryDeliverGoods(bakeryId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildings.TryAddLaborHours(bakeryId, 30, out string laborError), laborError);
            return bakeryId;
        }

        private static EntityId BuildAndFinishCrownStorehouse(Simulation sim, BuildingsModule buildings, GridPoint at)
        {
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", at, out EntityId id, out string error), error);
            buildings.TryDeliverGoods(id, "good.plank", 20, out _);
            buildings.TryDeliverGoods(id, "good.stone", 10, out _);
            buildings.TryAddLaborHours(id, 30, out _);
            return id;
        }

        [Test]
        public void AWorkerInConstantBadMood_QuitsAfterSevenDaysInARow()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            // A huge homeless mood penalty reliably clamps mood to 0 for the whole test, without the fragility of
            // forcing individual need meters (and without risking a starvation death, since food keeps hunger fed).
            sim.Settings.Set(PeopleModule.HomelessMoodPenaltySettingId, 100);

            EntityId storehouseId = BuildAndFinishCrownStorehouse(sim, buildings, new GridPoint(10, 10));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            inventory.Add(worker, "good.loaf", 10000);
            StepDays(sim, 1); // hired

            Assert.IsTrue(jobs.TryGetJobs(storehouseId, out JobsState state));
            Assert.AreEqual(worker, state.Worker.Occupants[0]);

            // Checked via the quit event rather than final occupancy: with no one else applying, a quitting
            // worker can be re-hired into the very same still-open slot the very same day (hiring runs right
            // after the quit check, in the same daily pass), which would otherwise mask the quit entirely.
            int quitCount = 0;
            sim.Events.Subscribe(JobsModule.QuitEventId, e =>
            {
                if (e.Data.GetEntity("person") == worker)
                {
                    quitCount++;
                }
            });

            StepDays(sim, 6); // 6 consecutive bad-mood days: not yet enough
            Assert.AreEqual(0, quitCount, "6 consecutive bad-mood days shouldn't be enough to quit.");

            StepDays(sim, 1); // the 7th
            sim.Step(); // the quit event, posted from inside the NewDayEvent handler, only delivers on the next tick
            Assert.AreEqual(1, quitCount, "7 consecutive bad-mood days should make the worker quit.");
        }

        [Test]
        public void AnOwner_FiresAWorkerWhoseSkillFallsFarBelowTheSlotsMinimum()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId storehouseId = BuildAndFinishCrownStorehouse(sim, buildings, new GridPoint(10, 10));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            StepDays(sim, 1); // hired with skill 0

            Assert.IsTrue(jobs.TryGetJobs(storehouseId, out JobsState state));
            Assert.AreEqual(worker, state.Worker.Occupants[0]);

            // Minimum skill 15, fire shortfall 5 by default: skill 0 is well under 15-5=10.
            Assert.IsTrue(jobs.TrySetGovernmentSlotConfig(storehouseId, JobSlotType.Worker, null, null, null, 15, null, out string setError), setError);

            StepDays(sim, 1);
            Assert.IsFalse(state.Worker.Occupants[0].HasValue, "A worker far below the slot's minimum skill should be fired.");
        }

        [Test]
        public void TheCrown_CanFireGovernmentStaff_ButNotPrivateStaff()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            // The bakery (higher, skilled pay) is filled first, so the later-hired, lower-paid storehouse worker
            // has no still-open higher-paying opening to be tempted away to during its own hiring pass.
            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);
            EntityId privateWorker = people.SpawnPerson(60, 60);
            StepDays(sim, 1);

            EntityId storehouseId = BuildAndFinishCrownStorehouse(sim, buildings, new GridPoint(10, 10));
            StepDays(sim, 1);
            EntityId governmentWorker = people.SpawnPerson(30, 30);
            StepDays(sim, 1);

            Assert.IsTrue(jobs.TryGetJobs(storehouseId, out JobsState storehouseState));
            Assert.AreEqual(governmentWorker, storehouseState.Worker.Occupants[0]);
            Assert.IsTrue(jobs.TryGetJobs(bakeryId, out JobsState bakeryState));
            Assert.AreEqual(privateWorker, bakeryState.Worker.Occupants[0]);

            string governmentResult = sim.Commands.Execute($"fire {governmentWorker}");
            Assert.IsFalse(storehouseState.Worker.Occupants[0].HasValue, governmentResult);

            string privateResult = sim.Commands.Execute($"fire {privateWorker}");
            Assert.AreEqual(privateWorker, bakeryState.Worker.Occupants[0], privateResult);
        }
    }
}
