using NUnit.Framework;
using TheEmptying.Core;
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
using TheEmptying.Modules.MapResources;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Wealth
{
    public class WealthModuleTransferTests
    {
        private static (Simulation Simulation, WealthModule Wealth, PeopleModule People) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealthModule = new WealthModule(people);
            var land = new LandModule(mapMovement, wealthModule);
            var buildings = new BuildingsModule(mapMovement, land, wealthModule, inventory, people);
            var jobs = new JobsModule(buildings, wealthModule, people, mapMovement);
            var market = new MarketModule(buildings, wealthModule, inventory, people, mapMovement);
            var mapResources = new MapResourcesModule(mapMovement, land);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement, mapResources);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealthModule, land, buildings, jobs, market, mapResources, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, wealthModule, people);
        }

        [Test]
        public void TryTransfer_WithEnough_Succeeds()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people) = Build(core);
            EntityId a = people.SpawnPerson(30, 30);
            EntityId b = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(a), Money.FromCopper(100));

            bool result = wealth.TryTransfer(AccountId.Person(a), AccountId.Person(b), Money.FromCopper(40), TransferReason.Wage);

            Assert.IsTrue(result);
            Assert.AreEqual(Money.FromCopper(60), wealth.GetBalance(AccountId.Person(a)));
            Assert.AreEqual(Money.FromCopper(40), wealth.GetBalance(AccountId.Person(b)));
        }

        [Test]
        public void TryTransfer_Unaffordable_FailsCleanlyAndLeavesBalancesUnchanged()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people) = Build(core);
            EntityId a = people.SpawnPerson(30, 30);
            EntityId b = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(a), Money.FromCopper(10));

            bool result = wealth.TryTransfer(AccountId.Person(a), AccountId.Person(b), Money.FromCopper(40), TransferReason.Wage);

            Assert.IsFalse(result);
            Assert.AreEqual(Money.FromCopper(10), wealth.GetBalance(AccountId.Person(a)));
            Assert.AreEqual(Money.Zero, wealth.GetBalance(AccountId.Person(b)));
        }

        [Test]
        public void TryTransfer_FromTheCrown_UnaffordableFailsTheSameWay()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people) = Build(core);
            EntityId a = people.SpawnPerson(30, 30);
            Money crownStart = wealth.GetBalance(AccountId.Crown);

            bool result = wealth.TryTransfer(AccountId.Crown, AccountId.Person(a), crownStart + Money.FromCopper(1), TransferReason.Grant);

            Assert.IsFalse(result);
            Assert.AreEqual(crownStart, wealth.GetBalance(AccountId.Crown));
        }

        [Test]
        public void Balances_NeverGoNegative()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people) = Build(core);
            EntityId a = people.SpawnPerson(30, 30);
            EntityId b = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(a), Money.FromCopper(5));

            wealth.TryTransfer(AccountId.Person(a), AccountId.Person(b), Money.FromCopper(100), TransferReason.Purchase);

            Assert.GreaterOrEqual(wealth.GetBalance(AccountId.Person(a)).HundredthsOfCopper, 0);
        }

        [Test]
        public void TryTransfer_RecordsBothSidesUnderTheRightReasonCategory()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people) = Build(core);
            EntityId a = people.SpawnPerson(30, 30);
            EntityId b = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(a), Money.FromCopper(100));

            wealth.TryTransfer(AccountId.Person(a), AccountId.Person(b), Money.FromCopper(25), TransferReason.Rent);

            Assert.AreEqual(Money.FromCopper(25), wealth.GetLedgerOut(AccountId.Person(a))[TransferReason.Rent]);
            Assert.AreEqual(Money.FromCopper(25), wealth.GetLedgerIn(AccountId.Person(b))[TransferReason.Rent]);
        }

        [Test]
        public void Ledger_RollsOverEachMonth()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people) = Build(core);
            EntityId a = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(a), Money.FromCopper(50), TransferReason.Grant);

            Assert.AreEqual(Money.FromCopper(50), wealth.GetLedgerIn(AccountId.Person(a))[TransferReason.Grant]);

            for (long i = 0; i < CalendarConstants.TicksPerMonth; i++)
            {
                sim.Step();
            }

            Assert.IsFalse(wealth.GetLedgerIn(AccountId.Person(a)).ContainsKey(TransferReason.Grant), "This month's ledger should have started fresh.");
            Assert.AreEqual(Money.FromCopper(50), wealth.GetLedgerIn(AccountId.Person(a), currentMonth: false)[TransferReason.Grant], "Last month's totals should have moved to Previous.");
        }

        [Test]
        public void Grant_CreditsWithNoPayerAndCannotFail()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, _) = Build(core);

            wealth.Grant(AccountId.Crown, Money.FromCopper(1000));

            Assert.AreEqual(wealth.GetBalance(AccountId.Crown), Money.FromCopper(5000) + Money.FromCopper(1000));
        }
    }
}
