using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
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
    public class JobsModuleSkillAndClassTests
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

        private static void StepMonths(Simulation sim, int months)
        {
            for (long i = 0; i < CalendarConstants.TicksPerMonth * months; i++)
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

        [Test]
        public void Skills_ImproveWithWork()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            inventory.Add(worker, "good.loaf", 10000);
            StepDays(sim, 1); // hired

            people.TryGetPerson(worker, out PersonState workerState);
            Assert.AreEqual(0, workerState.Skills[(int)Skill.Cooking]);

            StepDays(sim, 30);

            Assert.Greater(workerState.Skills[(int)Skill.Cooking], 0, "Working the job should have raised the relevant skill.");
        }

        [Test]
        public void WorkSpeed_FollowsSkill_HigherSkillAccumulatesWorkFaster()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            inventory.Add(worker, "good.loaf", 10000);
            StepDays(sim, 1); // hired

            Assert.IsTrue(jobs.TryGetJobs(bakeryId, out JobsState state));
            Assert.AreEqual(worker, state.Worker.Occupants[0]);

            StepDays(sim, 5); // five days at skill 0 (50% speed)
            double unskilledGain = state.Worker.WorkHoursAccumulated;

            people.TryGetPerson(worker, out PersonState workerState);
            workerState.Skills[(int)Skill.Cooking] = 20; // max: 150% speed

            double before = state.Worker.WorkHoursAccumulated;
            StepDays(sim, 5); // the same five days, now at skill 20
            double skilledGain = state.Worker.WorkHoursAccumulated - before;

            Assert.Greater(skilledGain, unskilledGain, "A skill-20 worker (150% speed) should out-accumulate the same worker's own skill-0 period (50% speed) over the same number of days.");
        }

        [Test]
        public void SkilledPrivateJob_MakesTheWorkerMiddleClass_AtTheNextMonthlyCheck()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            EntityId bakeryId = DiscoverAndBuildBakery(sim, buildings, land, wealth, inventory, owner, new GridPoint(50, 50));
            StepDays(sim, 1);

            EntityId worker = people.SpawnPerson(30, 30);
            inventory.Add(worker, "good.loaf", 10000);
            StepDays(sim, 1); // hired

            Assert.AreEqual(SocialClass.Lower, wealth.GetClass(worker));

            StepMonths(sim, 1);

            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(worker), "A skilled private job should make the worker middle class at the next monthly check.");
        }

        [Test]
        public void GovernmentJobs_DontSetClassDirectly_EvenASkilledOne()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, JobsModule jobs, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            // The builder slot on a government construction site is skilled (Construction), but it's still a
            // government job, so it shouldn't elevate class the way an equally skilled private job would.
            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(10, 10), out EntityId siteId, out string error), error);
            buildings.TryDeliverGoods(siteId, "good.plank", 20, out _);
            buildings.TryDeliverGoods(siteId, "good.stone", 10, out _);
            StepDays(sim, 1); // creates the builder opening

            EntityId builder = people.SpawnPerson(30, 30);
            inventory.Add(builder, "good.loaf", 10000);
            StepDays(sim, 1); // hired as builder

            StepMonths(sim, 2);

            Assert.AreEqual(SocialClass.Lower, wealth.GetClass(builder), "Government jobs (even skilled ones) don't set class directly.");
        }
    }
}
