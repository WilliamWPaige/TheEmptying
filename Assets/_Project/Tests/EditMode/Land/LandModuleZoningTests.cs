using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Land
{
    public class LandModuleZoningTests
    {
        private static (Simulation Simulation, LandModule Land, WealthModule Wealth) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, land, wealth);
        }

        [Test]
        public void PaintingAZone_SetsItOnEveryCellInTheRect()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, _) = Build(core);

            var rect = new CellRect(10, 10, 4, 4);
            land.RequestSetZone(rect, "zone.residential_low");
            sim.Step();

            foreach (GridPoint cell in rect.Cells())
            {
                Assert.AreEqual("zone.residential_low", land.GetZoneAt(cell));
            }
        }

        [Test]
        public void RemovingAZone_ClearsIt()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, _) = Build(core);

            var rect = new CellRect(10, 10, 4, 4);
            land.RequestSetZone(rect, "zone.residential_low");
            sim.Step();
            Assert.AreEqual("zone.residential_low", land.GetZoneAt(new GridPoint(11, 11)));

            land.RequestRemoveZone(rect);
            sim.Step();

            Assert.IsNull(land.GetZoneAt(new GridPoint(11, 11)));
        }

        [Test]
        public void CrownGrounds_CannotBeRezoned()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, _) = Build(core);

            CellRect grounds = land.CrownGrounds;
            land.RequestSetZone(grounds, "zone.commercial");
            sim.Step();

            foreach (GridPoint cell in grounds.Cells())
            {
                Assert.IsNull(land.GetZoneAt(cell), "Crown grounds must never take a zone.");
            }
        }

        [Test]
        public void CellsUnderAPlot_CannotBeRezoned()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land, WealthModule wealth) = Build(core);

            var rect = new CellRect(10, 10, 4, 4);
            land.RequestSetZone(rect, "zone.residential_low");
            sim.Step();

            EntityId buyer = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(buyer), Money.FromShil(10000));

            Assert.IsTrue(land.TryBuyPlot(buyer, "zone.residential_low", new GridPoint(10, 10), out _, out string error), error);

            land.RequestSetZone(rect, "zone.commercial");
            sim.Step();

            Assert.AreEqual("zone.residential_low", land.GetZoneAt(new GridPoint(11, 11)), "A cell under a plot must keep its original zone.");
        }
    }
}
