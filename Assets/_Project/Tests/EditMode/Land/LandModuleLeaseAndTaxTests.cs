using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Land
{
    public class LandModuleLeaseAndTaxTests
    {
        private static (Simulation Simulation, LandModule Land, WealthModule Wealth) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, land, wealth);
        }

        private static void StepMonths(Simulation sim, int months)
        {
            for (long i = 0; i < CalendarConstants.TicksPerMonth * months; i++)
            {
                sim.Step();
            }
        }

        [Test]
        public void Lease_ChargesMonthly()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();

            EntityId leaseholder = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(leaseholder), Money.FromShil(1000));

            Assert.IsTrue(land.TryLeasePlot(leaseholder, "zone.residential_low", new GridPoint(50, 50), out _, out string error), error);

            Money balanceBefore = wealth.GetBalance(AccountId.Person(leaseholder));
            Money crownBefore = wealth.GetBalance(AccountId.Crown);

            StepMonths(sim, 1);

            Money expectedLease = Money.FromShil(3000 * 0.02); // 2% of the base 3,000 shil price
            Assert.AreEqual(balanceBefore - expectedLease, wealth.GetBalance(AccountId.Person(leaseholder)));
            Assert.AreEqual(crownBefore + expectedLease, wealth.GetBalance(AccountId.Crown));
        }

        [Test]
        public void UnpaidLease_EndsAfterOneMonth_AndThePlotReturnsToTheCrown()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();

            EntityId leaseholder = sim.Entities.Create(EntityKind.Person);
            // No money granted: the first lease charge will fail.
            Assert.IsTrue(land.TryLeasePlot(leaseholder, "zone.residential_low", new GridPoint(50, 50), out EntityId plotId, out string error), error);

            int endedCount = 0;
            sim.Events.Subscribe(LandModule.LeaseEndedEventId, _ => endedCount++);

            StepMonths(sim, 1);

            Assert.AreEqual(1, endedCount);
            Assert.IsTrue(land.TryGetPlot(plotId, out PlotState plot));
            Assert.IsNull(plot.Leaseholder);
        }

        [Test]
        public void LandTax_IsChargedMonthlyToOwnersOnly_NotLeaseholdersOrTheCrown()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            land.RequestSetZone(new CellRect(60, 60, 4, 4), "zone.residential_low");
            sim.Step();

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(owner), Money.FromShil(10000));
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out _, out string buyError), buyError);

            EntityId leaseholder = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(leaseholder), Money.FromShil(10000));
            Assert.IsTrue(land.TryLeasePlot(leaseholder, "zone.residential_low", new GridPoint(60, 60), out _, out string leaseError), leaseError);

            Money ownerBalanceBefore = wealth.GetBalance(AccountId.Person(owner));

            StepMonths(sim, 1);

            Assert.AreEqual(ownerBalanceBefore - Money.FromShil(20), wealth.GetBalance(AccountId.Person(owner)), "Owner should be charged exactly the residential-low land tax.");
        }

        [Test]
        public void UnpaidTax_BuildsDebt_AndTheEventFiresOnlyOnce()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(owner), Money.FromShil(3000)); // exactly enough to buy, nothing left for tax
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out EntityId plotId, out string error), error);

            int unpaidCount = 0;
            sim.Events.Subscribe(LandModule.TaxUnpaidEventId, _ => unpaidCount++);

            StepMonths(sim, 1);
            land.TryGetPlot(plotId, out PlotState plotAfterMonth1);
            Assert.AreEqual(Money.FromShil(20), plotAfterMonth1.TaxDebt);
            Assert.AreEqual(1, unpaidCount);

            StepMonths(sim, 1);
            land.TryGetPlot(plotId, out PlotState plotAfterMonth2);
            Assert.AreEqual(Money.FromShil(40), plotAfterMonth2.TaxDebt, "Debt should keep accumulating.");
            Assert.AreEqual(1, unpaidCount, "The unpaid event should only fire the first time debt starts.");
        }
    }
}
