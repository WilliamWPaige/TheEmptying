using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
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

namespace TheEmptying.Tests.EditMode.Land
{
    public class LandModulePricingTests
    {
        private static (Simulation Simulation, LandModule Land) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            var mapResources = new MapResourcesModule(mapMovement, land);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement, mapResources);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, land);
        }

        [Test]
        public void BasePrice_FarFromTheCrownGrounds_MatchesTheZoneSetting()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land) = Build(core);

            var farRect = new CellRect(2, 2, 4, 4);
            Assert.AreEqual(Money.FromCopper(3000), land.GetCurrentPrice("zone.residential_low", farRect));
        }

        [Test]
        public void LocationModifier_IsHighestRightNextToTheCrownGrounds_AndZeroFarAway()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land) = Build(core);

            CellRect grounds = land.CrownGrounds;
            var adjacentRect = new CellRect(grounds.X - 4, grounds.Y, 4, 4); // touching the grounds' west edge
            var farRect = new CellRect(2, 2, 4, 4);

            Money adjacentPrice = land.GetCurrentPrice("zone.residential_low", adjacentRect);
            Money farPrice = land.GetCurrentPrice("zone.residential_low", farRect);

            Assert.Greater(adjacentPrice.HundredthsOfCopper, farPrice.HundredthsOfCopper, "A plot right next to the crown grounds should cost more than one far away.");
            Assert.AreEqual(Money.FromCopper(3000), farPrice);
            Assert.LessOrEqual(adjacentPrice.HundredthsOfCopper, Money.FromCopper(3000).ApplyPercent(50.0).HundredthsOfCopper, "The modifier should never exceed the configured maximum.");
        }

        [Test]
        public void ChangingThePriceSetting_ChangesThePrice()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, LandModule land) = Build(core);

            sim.Settings.Set("setting.land.plot_price.residential_low", 5000);

            var farRect = new CellRect(2, 2, 4, 4);
            Assert.AreEqual(Money.FromCopper(5000), land.GetCurrentPrice("zone.residential_low", farRect));
        }
    }
}
