using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
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

namespace TheEmptying.Tests.EditMode.People
{
    /// <summary>3.9b: a new game's 8 founders (one the advisor) and a separate monarch, replacing the old 8 ordinary immigrants.</summary>
    public class PeopleModuleFoundersTests
    {
        private static (Simulation Simulation, PeopleModule People, WealthModule Wealth) Build(TempCoreContentCopy core)
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
            return (simulation, people, wealth);
        }

        [Test]
        public void NewGame_CreatesEightFoundersAndASeparateMonarch()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            var founders = people.People.Where(row => row.Value.IsFounder).ToList();
            var monarchs = people.People.Where(row => row.Value.IsMonarch).ToList();

            Assert.AreEqual(8, founders.Count, "Expected 8 founders.");
            Assert.AreEqual(1, monarchs.Count, "Expected exactly one monarch.");
            Assert.AreEqual(9, people.People.Count(), "8 founders plus the monarch.");
            Assert.IsFalse(monarchs[0].Value.IsFounder, "The monarch is a separate person, not a founder.");
        }

        [Test]
        public void TheAdvisor_IsOneOfTheFounders()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            Assert.IsTrue(people.AdvisorId.HasValue);
            Assert.IsTrue(people.IsFounder(people.AdvisorId.Value), "The advisor should be one of the 8 founders.");
            Assert.IsTrue(people.IsAdvisor(people.AdvisorId.Value));
            Assert.AreNotEqual(people.MonarchId, people.AdvisorId);
        }

        [Test]
        public void Founders_StartMaxedInLaborSkills_AndAverageElsewhere()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            foreach (var row in people.People.Where(r => r.Value.IsFounder))
            {
                PersonState founder = row.Value;
                Assert.AreEqual(20, founder.Skills[(int)Skill.Construction]);
                Assert.AreEqual(20, founder.Skills[(int)Skill.Mining]);
                Assert.AreEqual(20, founder.Skills[(int)Skill.Logging]);
                Assert.AreEqual(20, founder.Skills[(int)Skill.Hauling]);
                Assert.AreEqual(20, founder.Skills[(int)Skill.Farming]);
                Assert.AreEqual(10, founder.Skills[(int)Skill.Cooking], "Everything else should be the average level.");
            }
        }

        [Test]
        public void Founders_HaveTheLongerLifespanRange_AndNeverRefuseFlagSet()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, _) = Build(core);

            foreach (var row in people.People.Where(r => r.Value.IsFounder))
            {
                PersonState founder = row.Value;
                Assert.IsTrue(founder.NeverRefusesOrFails);
                Assert.GreaterOrEqual(founder.LifespanYears, 80);
                Assert.LessOrEqual(founder.LifespanYears, 90);
            }
        }

        [Test]
        public void FoundersAndMonarch_AreHousedInTheKeep_ForFree_WithinADay()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, WealthModule wealth) = Build(core);

            for (long i = 0; i < Core.Timekeeping.CalendarConstants.TicksPerDay * 2; i++)
            {
                sim.Step();
            }

            foreach (var row in people.People)
            {
                Assert.IsFalse(people.IsHomeless(row.Key), $"{row.Value.FullName} should be housed in the keep within the first couple of days.");
            }

            // Free lodging: nobody should have paid any rent to get there.
            foreach (var row in people.People.Where(r => r.Value.IsFounder))
            {
                Assert.AreEqual(0, wealth.GetBalance(AccountId.Person(row.Key)).HundredthsOfShil,
                    "A founder starts and stays with no money; housing must be free.");
            }
        }
    }
}
