using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
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

namespace TheEmptying.Tests.EditMode.Wealth
{
    public class WealthModuleClassTests
    {
        private static (Simulation Simulation, WealthModule Wealth, PeopleModule People, InventoryModule Inventory) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealthModule = new WealthModule(people);
            var land = new LandModule(mapMovement, wealthModule);
            var buildings = new BuildingsModule(mapMovement, land, wealthModule, inventory, people);
            var jobs = new JobsModule(buildings, wealthModule, people, mapMovement);
            var market = new MarketModule(buildings, wealthModule, inventory, people, mapMovement);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealthModule, land, buildings, jobs, market, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            // These tests step several months to exercise the monthly class check; everyone
            // spawned here is homeless by definition (3.4, no housing yet), and the resulting
            // health damage would otherwise kill a test person well before the test is done.
            simulation.Settings.Set(PeopleModule.HomelessHealthDamagePerNightSettingId, 0);

            return (simulation, wealthModule, people, inventory);
        }

        /// <summary>
        /// SpawnPerson's default month of provisions runs out well before these
        /// multi-month class tests do; top up with enough loaves that starvation
        /// never becomes the thing being tested by accident.
        /// </summary>
        private static EntityId SpawnWellFedPerson(PeopleModule people, InventoryModule inventory, int minAge, int maxAge)
        {
            EntityId id = people.SpawnPerson(minAge, maxAge);
            inventory.Add(id, "good.loaf", 10000);
            return id;
        }

        private static void StepMonths(Simulation sim, int months)
        {
            for (long i = 0; i < CalendarConstants.TicksPerMonth * months; i++)
            {
                sim.Step();
            }
        }

        [Test]
        public void ClassThresholds_FollowTheLaborerWageSetting()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            sim.Settings.Set(WealthModule.LaborerWagePerDaySettingId, 10); // middle at 60*10 = 600 copper
            EntityId id = SpawnWellFedPerson(people, inventory, 30, 30);
            wealth.Grant(AccountId.Person(id), Money.FromCopper(600));

            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(id), "600 copper at wage 10 is exactly 60 days - middle class.");

            sim.Settings.Set(WealthModule.LaborerWagePerDaySettingId, 20); // middle now needs 1200 copper
            StepMonths(sim, 2); // two months below, to clear the drop grace period
            Assert.AreEqual(SocialClass.Lower, wealth.GetClass(id), "The same 600 copper is only 30 days at wage 20 - no longer middle.");
        }

        [Test]
        public void Rising_AppliesAtTheNextMonthlyCheck()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);
            EntityId id = SpawnWellFedPerson(people, inventory, 30, 30);

            wealth.Grant(AccountId.Person(id), Money.FromCopper(2000)); // well past the upper threshold (600 days * 20 = 12000... use middle instead)
            wealth.Grant(AccountId.Person(id), Money.FromCopper(800)); // total 2800, comfortably middle (60*20=1200) but short of upper (600*20=12000)

            StepMonths(sim, 1);

            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(id), "Rising takes effect at the very next monthly check, no delay.");
        }

        [Test]
        public void Dropping_OnlyAfterTwoMonthsInARowBelow()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);
            EntityId a = SpawnWellFedPerson(people, inventory, 30, 30);
            EntityId sink = SpawnWellFedPerson(people, inventory, 30, 30);

            wealth.Grant(AccountId.Person(a), Money.FromCopper(1500)); // middle (needs 1200 at the default wage 20)
            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(a));

            wealth.TryTransfer(AccountId.Person(a), AccountId.Person(sink), Money.FromCopper(1400), TransferReason.Purchase); // down to 100, well below middle

            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(a), "One month below should not drop class yet.");

            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Lower, wealth.GetClass(a), "A second month in a row below should drop class.");
        }

        [Test]
        public void Class_TakesTheHighestOfWealthJobAndPowerTiers()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);
            EntityId id = SpawnWellFedPerson(people, inventory, 30, 30); // near-zero wealth: Lower on its own

            wealth.SetJobTierProvider(entity => entity == id ? SocialClass.Middle : SocialClass.Lower);
            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(id), "Job tier should lift a poor person to middle.");

            wealth.SetJobTierProvider(_ => SocialClass.Lower);
            wealth.SetPowerProvider(entity => entity == id ? SocialClass.Upper : (SocialClass?)null);
            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Upper, wealth.GetClass(id), "A held post should lift class to upper while active.");
        }

        [Test]
        public void Children_InheritParentsClassUntilAdulthood()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId parent = SpawnWellFedPerson(people, inventory, 30, 30);
            wealth.Grant(AccountId.Person(parent), Money.FromCopper(1500));
            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(parent));

            EntityId child = SpawnWellFedPerson(people, inventory, 5, 5);
            wealth.SetParent(child, parent);
            StepMonths(sim, 1);

            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(child), "A child with no money of their own should take their parent's class.");

            people.TryGetPerson(child, out PersonState childState);
            childState.AgeLifeYears = (int)sim.Settings.Get(PeopleModule.AdultAgeYearsSettingId) + 1;
            StepMonths(sim, 2); // two months below, so a prior inherited class has time to drop on its own merits

            Assert.AreEqual(SocialClass.Lower, wealth.GetClass(child), "An adult with no money of their own should be judged on their own wealth.");
        }

        [Test]
        public void ClassChangedEvent_FiresOnlyOnRealChanges()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);
            EntityId id = SpawnWellFedPerson(people, inventory, 30, 30);

            int changedCount = 0;
            sim.Events.Subscribe(WealthModule.ClassChangedEventId, e =>
            {
                if (e.Data.GetEntity("person") == id)
                {
                    changedCount++;
                }
            });

            StepMonths(sim, 2); // stays lower class the whole time - no change
            Assert.AreEqual(0, changedCount);

            wealth.Grant(AccountId.Person(id), Money.FromCopper(1500));
            StepMonths(sim, 1); // rises to middle - one change
            sim.Step(); // class.changed queues for the tick after the month boundary that posted it
            Assert.AreEqual(1, changedCount);

            StepMonths(sim, 2); // stays middle - no further change
            sim.Step();
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void RisingToMiddleClass_AddsComfortRecreationAndVariedFoodNeeds()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);
            EntityId id = SpawnWellFedPerson(people, inventory, 30, 30);

            people.TryGetPerson(id, out PersonState before);
            Assert.IsFalse(before.Needs.ContainsKey("need.comfort"));

            wealth.Grant(AccountId.Person(id), Money.FromCopper(1500));
            StepMonths(sim, 1);
            Assert.AreEqual(SocialClass.Middle, wealth.GetClass(id));

            // The class.changed handler runs one tick after the month boundary (events queue for the next DeliverPending); give it a moment.
            sim.Step();

            people.TryGetPerson(id, out PersonState after);
            Assert.IsTrue(after.Needs.ContainsKey("need.comfort"));
            Assert.IsTrue(after.Needs.ContainsKey("need.recreation"));
            Assert.IsTrue(after.Needs.ContainsKey("need.varied_food"));
        }
    }
}
