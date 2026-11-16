using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Land
{
    public class LandModulePlotTests
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

        private static void PaintAndApply(Simulation sim, LandModule land, CellRect rect, string zoneId)
        {
            land.RequestSetZone(rect, zoneId);
            sim.Step();
        }

        [Test]
        public void Plot_MustFitInsideOneMatchingZone_OnFreeCells()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            PaintAndApply(sim, land, new CellRect(50, 50, 4, 4), "zone.residential_low");

            EntityId buyer = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(10000));

            Assert.IsTrue(land.TryBuyPlot(buyer, "zone.residential_low", new GridPoint(50, 50), out _, out string error), error);

            EntityId buyer2 = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(buyer2), Money.FromShil(10000));

            Assert.IsFalse(land.TryBuyPlot(buyer2, "zone.residential_low", new GridPoint(100, 100), out _, out string unzonedError), "An unzoned spot must be refused.");
            Assert.IsNotNull(unzonedError);
        }

        [Test]
        public void OverlappingPlots_AreRefused()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            PaintAndApply(sim, land, new CellRect(50, 50, 8, 8), "zone.residential_low");

            EntityId a = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(a), Money.FromShil(10000));
            Assert.IsTrue(land.TryBuyPlot(a, "zone.residential_low", new GridPoint(50, 50), out _, out string firstError), firstError);

            EntityId b = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(b), Money.FromShil(10000));
            Assert.IsFalse(land.TryBuyPlot(b, "zone.residential_low", new GridPoint(52, 52), out _, out string error), "A plot overlapping an existing one must be refused.");
            Assert.IsNotNull(error);
        }

        [Test]
        public void Buying_TransfersMoneyAndOwnership()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            PaintAndApply(sim, land, new CellRect(50, 50, 4, 4), "zone.residential_low");

            EntityId buyer = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(10000));

            Assert.IsTrue(land.TryBuyPlot(buyer, "zone.residential_low", new GridPoint(50, 50), out EntityId plotId, out string error), error);

            Assert.AreEqual(Money.FromShil(10000) - Money.FromShil(3000), wealth.GetBalance(AccountId.Person(buyer)));
            Assert.AreEqual(Money.FromShil(5000) + Money.FromShil(3000), wealth.GetBalance(AccountId.Crown));
            Assert.IsTrue(land.TryGetPlot(plotId, out PlotState plot));
            Assert.AreEqual(buyer, plot.Owner);
        }

        [Test]
        public void Buying_FailsCleanlyWhenUnaffordable()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            PaintAndApply(sim, land, new CellRect(50, 50, 4, 4), "zone.residential_low");

            EntityId buyer = sim.Entities.Create(EntityKind.Person);
            // No grant: buyer has nothing.

            bool success = land.TryBuyPlot(buyer, "zone.residential_low", new GridPoint(50, 50), out _, out string error);

            Assert.IsFalse(success);
            Assert.IsNotNull(error);
            Assert.AreEqual(Money.Zero, wealth.GetBalance(AccountId.Person(buyer)));
            Assert.IsTrue(land.GetZoneAt(new GridPoint(50, 50)) == "zone.residential_low");
        }

        [Test]
        public void AnOwnersWealth_IncludesTheirPlots()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);
            PaintAndApply(sim, land, new CellRect(50, 50, 4, 4), "zone.residential_low");

            EntityId buyer = sim.Entities.Create(EntityKind.Person);
            Money startingCash = Money.FromShil(3000);
            wealth.Grant(AccountId.Person(buyer), startingCash);

            Money wealthBeforeBuying = wealth.GetWealth(buyer);

            Assert.IsTrue(land.TryBuyPlot(buyer, "zone.residential_low", new GridPoint(50, 50), out _, out string error), error);

            Assert.AreEqual(wealthBeforeBuying, wealth.GetWealth(buyer), "Spending cash on a plot of equal value should not change total wealth.");
        }
    }
}
