using System.IO;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
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
    public class LandModuleSaveTests
    {
        private static IModule[] Modules(out LandModule land, out WealthModule wealth)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            wealth = new WealthModule(people);
            land = new LandModule(mapMovement, wealth);
            return new IModule[] { mapMovement, inventory, people, wealth, land };
        }

        /// <summary>Paints a zone, buys one plot, leases another and leaves one owner's tax unpaid, so every piece of Land's state has something in it.</summary>
        private static void PopulateLandState(Simulation sim, LandModule land, WealthModule wealth)
        {
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            land.RequestSetZone(new CellRect(60, 60, 4, 4), "zone.residential_low");
            sim.Step();

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(owner), Money.FromShil(3000)); // exactly enough to buy, nothing left for tax
            land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out _, out _);

            EntityId leaseholder = sim.Entities.Create(EntityKind.Person);
            wealth.Grant(AccountId.Person(leaseholder), Money.FromShil(1000));
            land.TryLeasePlot(leaseholder, "zone.residential_low", new GridPoint(60, 60), out _, out _);
        }

        [Test]
        public void SaveAndLoad_WithZonesPlotsLeasesAndDebt_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 42;
            const int n = 40; // before the save, mid-month
            const int m = (int)CalendarConstants.TicksPerMonth; // after the load, crossing a month boundary (tax/lease charges, debt)

            IModule[] uninterruptedModules = Modules(out LandModule uninterruptedLand, out WealthModule uninterruptedWealth);
            (Simulation uninterruptedSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, uninterruptedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            PopulateLandState(uninterruptedSim, uninterruptedLand, uninterruptedWealth);

            for (int i = 0; i < n + m; i++)
            {
                uninterruptedSim.Step();
            }

            ulong expectedHash = uninterruptedSim.ComputeStateHash();

            IModule[] firstModules = Modules(out LandModule firstLand, out WealthModule firstWealth);
            (Simulation firstSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, firstModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            PopulateLandState(firstSim, firstLand, firstWealth);

            for (int i = 0; i < n; i++)
            {
                firstSim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 2 }, stream);
            stream.Position = 0;

            IModule[] secondModules = Modules(out LandModule _, out WealthModule _);
            (Simulation secondSim, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (int i = 0; i < m; i++)
            {
                secondSim.Step();
            }

            Assert.AreEqual(expectedHash, secondSim.ComputeStateHash());
        }
    }
}
