using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
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
    public class BuildingsModuleDiscoveryTests
    {
        private static (Simulation Simulation, BuildingsModule Buildings, LandModule Land, WealthModule Wealth) Build(TempCoreContentCopy core)
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
            return (simulation, buildings, land, wealth);
        }

        private static EntityId SetUpPlotOwner(Simulation sim, LandModule land, WealthModule wealth, string zoneId, GridPoint at)
        {
            EntityId owner = sim.Entities.Create(EntityKind.Person);
            land.RequestSetZone(new CellRect(at.X, at.Y, 6, 6), zoneId);
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(10000));
            Assert.IsTrue(land.TryBuyPlot(owner, zoneId, at, out _, out string error), error);
            return owner;
        }

        [Test]
        public void FirstRequestForANonBasicType_RaisesExactlyOneQuestion()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth) = Build(core);
            EntityId owner = SetUpPlotOwner(sim, land, wealth, "zone.commercial", new GridPoint(50, 50));

            int questionsAsked = 0;
            sim.Events.Subscribe(RuleRunner.QuestionAskedEventId, _ => questionsAsked++);

            Assert.IsFalse(buildingsModule.TryRequestPrivateBuilding(owner, "building.bakery", out _, out string error1));
            Assert.IsNotNull(error1);
            sim.Step(); // QuestionAskedEventId is only delivered on the next tick
            Assert.AreEqual(1, questionsAsked);

            // A second request for the same still-undecided type must not raise a second question.
            Assert.IsFalse(buildingsModule.TryRequestPrivateBuilding(owner, "building.bakery", out _, out string error2));
            Assert.IsNotNull(error2);
            sim.Step();
            Assert.AreEqual(1, questionsAsked);
        }

        [Test]
        public void AnsweringAllow_LetsTheOriginalRequesterBuildAfterward()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth) = Build(core);
            EntityId owner = SetUpPlotOwner(sim, land, wealth, "zone.commercial", new GridPoint(50, 50));

            buildingsModule.TryRequestPrivateBuilding(owner, "building.bakery", out _, out _);

            long questionId = sim.RuleRunner.PendingQuestions[0].QuestionId;
            string rejection = sim.AnswerQuestion(questionId, 1); // Allow
            Assert.IsNull(rejection);
            sim.Step(); // ProcessQuestionAnswers runs at the start of the next tick

            bool found = false;
            foreach (var row in buildingsModule.Buildings)
            {
                if (row.Value.BuildingTypeId == "building.bakery" && row.Value.Owner == owner)
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "Allowing the discovery should place the originally requested building.");
        }

        [Test]
        public void AnsweringForbid_RefusesAllFutureRequestsWithNoFurtherPopup()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth) = Build(core);
            EntityId owner = SetUpPlotOwner(sim, land, wealth, "zone.commercial", new GridPoint(50, 50));

            buildingsModule.TryRequestPrivateBuilding(owner, "building.bakery", out _, out _);
            long questionId = sim.RuleRunner.PendingQuestions[0].QuestionId;
            sim.AnswerQuestion(questionId, 2); // Forbid
            sim.Step();

            int questionsAsked = 0;
            sim.Events.Subscribe(RuleRunner.QuestionAskedEventId, _ => questionsAsked++);

            Assert.IsFalse(buildingsModule.TryRequestPrivateBuilding(owner, "building.bakery", out _, out string error));
            Assert.IsNotNull(error);
            Assert.AreEqual(0, questionsAsked, "A forbidden, already-decided type must never raise another popup.");
        }

        [Test]
        public void ConfigChange_AppliesToTheNextRequest()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth) = Build(core);
            EntityId owner = SetUpPlotOwner(sim, land, wealth, "zone.commercial", new GridPoint(50, 50));

            buildingsModule.TryRequestPrivateBuilding(owner, "building.bakery", out _, out _);
            long questionId = sim.RuleRunner.PendingQuestions[0].QuestionId;
            sim.AnswerQuestion(questionId, 1); // Allow, with a default max count
            sim.Step();

            Assert.IsTrue(buildingsModule.TrySetDiscoveredTypeConfig("building.bakery", allowed: null, maxCount: 0, allowedClassesMask: null, out string setError), setError);

            Assert.IsFalse(buildingsModule.TryRequestPrivateBuilding(owner, "building.bakery", out _, out string error), "A max count of 0 should refuse any further request.");
            Assert.IsNotNull(error);
        }
    }
}
