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
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Jobs
{
    public class JobsModuleShiftsAndPayTests
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
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

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

        private static void StepUntilHour(Simulation sim, int hour)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                if (GameDate.FromTick(sim.Clock.CurrentTick).Hour == hour)
                {
                    return;
                }

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
        public void AHiredWorker_CommutesAndWorksTheirHours_AndIsOffOtherwise()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            StepDays(sim, 1); // hired

            Assert.IsTrue(jobs.TryGetJobs(bakeryId, out JobsState state));
            Assert.AreEqual(worker, state.Worker.Occupants[0]);

            StepUntilHour(sim, 10); // well inside the 7-15 day shift
            sim.Step(); // give the arrival event a tick to resolve into Working
            people.TryGetPerson(worker, out PersonState workerState);
            Assert.AreEqual(PersonActivity.Working, workerState.Activity, "A worker should be at work during their shift.");

            StepUntilHour(sim, 20); // well outside the day shift, and the bakery has no night coverage
            Assert.AreNotEqual(PersonActivity.Working, workerState.Activity);
            Assert.AreNotEqual(PersonActivity.Commuting, workerState.Activity);
        }

        [Test]
        public void NightCoverage_IsHonored_AndMinimumOnDutyIsStored()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId storehouseId = BuildAndFinishCrownStorehouse(sim, buildings, new GridPoint(10, 10));
            StepDays(sim, 1);

            Assert.IsTrue(jobs.TrySetGovernmentSlotConfig(storehouseId, JobSlotType.Worker, payPerDay: null, coverage: JobCoverage.Night, minimumOnDuty: 1, minimumSkill: null, allowedClassesMask: null, out string setError), setError);

            EntityId worker = people.SpawnPerson(30, 30);
            StepDays(sim, 1); // hired

            Assert.IsTrue(jobs.TryGetJobs(storehouseId, out JobsState state));
            Assert.AreEqual(worker, state.Worker.Occupants[0]);
            Assert.AreEqual(1, state.Worker.MinimumOnDuty);
            Assert.AreEqual(JobCoverage.Night, state.Worker.Coverage);

            StepUntilHour(sim, 22); // inside the 19-3 night shift
            sim.Step();
            people.TryGetPerson(worker, out PersonState workerState);
            Assert.AreEqual(PersonActivity.Working, workerState.Activity);

            StepUntilHour(sim, 10); // well inside the day, outside the night shift
            Assert.AreNotEqual(PersonActivity.Working, workerState.Activity);
        }

        [Test]
        public void Wages_MoveDailyFromEmployerToWorker()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            StepDays(sim, 1); // hired

            Money ownerBefore = wealth.GetBalance(AccountId.Person(owner));
            Money workerBefore = wealth.GetBalance(AccountId.Person(worker));

            StepDays(sim, 1); // one full day's shift end

            Assert.Greater(wealth.GetBalance(AccountId.Person(worker)).HundredthsOfShil, workerBefore.HundredthsOfShil);
            Assert.Less(wealth.GetBalance(AccountId.Person(owner)).HundredthsOfShil, ownerBefore.HundredthsOfShil);
        }

        [Test]
        public void UnpaidWorker_QuitsAfterTwoDaysInARow()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            StepDays(sim, 1); // hired

            // Drain the owner so wages can't be paid.
            Money ownerBalance = wealth.GetBalance(AccountId.Person(owner));
            EntityId sink = sim.Entities.Create(EntityKind.Person);
            wealth.TryTransfer(AccountId.Person(owner), AccountId.Person(sink), ownerBalance, TransferReason.Other);

            Assert.IsTrue(jobs.TryGetJobs(bakeryId, out JobsState state));
            Assert.AreEqual(worker, state.Worker.Occupants[0]);

            // Checked via the quit event rather than final occupancy: with no one else applying, a worker who
            // quits for unpaid wages can be re-hired into the very same still-open slot the next day, which
            // would otherwise mask the quit if this test stepped far enough to also cross that next hiring pass.
            int quitCount = 0;
            sim.Events.Subscribe(JobsModule.QuitEventId, e =>
            {
                if (e.Data.GetEntity("person") == worker)
                {
                    quitCount++;
                }
            });

            StepDays(sim, 1); // day 1 unpaid
            Assert.AreEqual(0, quitCount, "One unpaid day shouldn't be enough to quit.");

            StepDays(sim, 1); // day 2 unpaid
            Assert.AreEqual(1, quitCount, "Two unpaid days in a row should make the worker quit.");
        }

        [Test]
        public void ACrownWithNoMoney_CantPayGovernmentStaff()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId storehouseId = BuildAndFinishCrownStorehouse(sim, buildings, new GridPoint(10, 10));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            StepDays(sim, 1); // hired

            // Drain the crown treasury entirely.
            Money crownBalance = wealth.GetBalance(AccountId.Crown);
            EntityId sink = sim.Entities.Create(EntityKind.Person);
            wealth.TryTransfer(AccountId.Crown, AccountId.Person(sink), crownBalance, TransferReason.Other);

            Assert.IsTrue(jobs.TryGetJobs(storehouseId, out JobsState state));
            Assert.AreEqual(worker, state.Worker.Occupants[0]);

            Money workerBalanceBefore = wealth.GetBalance(AccountId.Person(worker));
            StepDays(sim, 1);

            Assert.AreEqual(workerBalanceBefore.HundredthsOfShil, wealth.GetBalance(AccountId.Person(worker)).HundredthsOfShil, "A penniless crown can't pay, so the worker's balance shouldn't move.");
        }
    }
}
