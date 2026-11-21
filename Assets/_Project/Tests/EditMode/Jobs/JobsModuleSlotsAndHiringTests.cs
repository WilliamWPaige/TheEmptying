using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
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
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Jobs
{
    public class JobsModuleSlotsAndHiringTests
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
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            // The 8 starting founders would otherwise compete for the same handful of openings these tests set up.
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
                // Undiscovered: this raises the question. Answering "Allow" makes BuildingsModule retry the
                // original request internally, so we must not call TryRequestPrivateBuilding again ourselves -
                // the plot would already have a building on it ("one building per plot") and the retry would fail.
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

                Assert.AreNotEqual(EntityId.None, bakeryId, "Answering 'Allow' should have let BuildingsModule's internal retry build the bakery.");
            }

            inventory.Add(owner, "good.plank", 20);
            inventory.Add(owner, "good.stone", 10);
            Assert.IsTrue(buildings.TryDeliverGoods(bakeryId, "good.plank", 20, out string plankError), plankError);
            Assert.IsTrue(buildings.TryDeliverGoods(bakeryId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildings.TryAddLaborHours(bakeryId, 30, out string laborError), laborError);
            return bakeryId;
        }

        [Test]
        public void WorkerSlots_AppearOnceTheBuildingFinishes_AndAreOpenings()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));

            StepDays(sim, 1); // lets Jobs' daily sync pick up the newly finished building

            Assert.IsTrue(jobs.TryGetJobs(bakeryId, out JobsState state));
            Assert.IsNotNull(state.Worker);
            Assert.AreEqual(1, state.Worker.Occupants.Count);
            Assert.IsFalse(state.Worker.Occupants[0].HasValue, "A freshly finished bakery's worker slot should start vacant - an opening.");
        }

        [Test]
        public void BestSkilledApplicant_GetsTheJob()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId unskilled = people.SpawnPerson(30, 30);
            EntityId skilled = people.SpawnPerson(30, 30);
            people.TryGetPerson(skilled, out PersonState skilledState);
            skilledState.Skills[(int)Skill.Cooking] = 10;

            StepDays(sim, 1); // the daily application/hiring pass

            Assert.IsTrue(jobs.TryGetJobs(bakeryId, out JobsState state));
            Assert.AreEqual(skilled, state.Worker.Occupants[0]);
        }

        [Test]
        public void AnEmployedPerson_OnlySwitches_ForAtLeast20PercentMorePay()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId ownerA = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryA = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, ownerA, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            StepDays(sim, 1); // hired at bakery A

            Assert.IsTrue(jobs.TryGetJobs(bakeryA, out JobsState stateA));
            Assert.AreEqual(worker, stateA.Worker.Occupants[0]);
            Money payA = stateA.Worker.PayPerDay;

            EntityId ownerB = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryB = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, ownerB, new GridPoint(70, 70));
            StepDays(sim, 1);

            Assert.IsTrue(jobs.TryGetJobs(bakeryB, out JobsState stateB));

            // Just under the 20% bar: should not switch.
            stateB.Worker.PayPerDay = Money.FromHundredthsOfShil((long)(payA.HundredthsOfShil * 1.19));
            StepDays(sim, 1);
            Assert.AreEqual(worker, stateA.Worker.Occupants[0], "A raise under 20% should not be enough to switch.");
            Assert.IsFalse(stateB.Worker.Occupants[0].HasValue);

            // At least 20% more: should switch.
            stateB.Worker.PayPerDay = Money.FromHundredthsOfShil((long)(payA.HundredthsOfShil * 1.21));
            StepDays(sim, 1);
            Assert.IsFalse(stateA.Worker.Occupants[0].HasValue, "The worker should have left bakery A for the better-paying opening.");
            Assert.AreEqual(worker, stateB.Worker.Occupants[0]);
        }
    }
}
