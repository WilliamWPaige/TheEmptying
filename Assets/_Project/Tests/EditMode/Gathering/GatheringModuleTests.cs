using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
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

namespace TheEmptying.Tests.EditMode.Gathering
{
    /// <summary>3.9b: hand gathering - founders build crown sites and gather by hand, anyone jobless may gather and sell to the crown.</summary>
    public class GatheringModuleTests
    {
        private static (Simulation Simulation, GatheringModule Gathering, BuildingsModule Buildings, InventoryModule Inventory, WealthModule Wealth, MarketModule Market, PeopleModule People) Build(TempCoreContentCopy core)
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
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, gathering, buildings, inventory, wealth, market, people);
        }

        [Test]
        public void Founders_GatherWood_IntoTheStockpile_WithNoOrdersOrHelp()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // Plenty of wheat already, so the priority order moves straight past farming to whichever
            // raw material (logs or stone) is scarcer - here, logs (the stockpile starts with some stone).
            inventory.Add(storehouseId, "good.wheat", 10000);

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10; i++)
            {
                sim.Step();
            }

            Assert.Greater(inventory.GetCount(storehouseId, "good.log"), 0,
                "Founders with no job and no orders should have hand-gathered some logs into the stockpile within 10 days.");
        }

        [Test]
        public void Founders_FarmWheat_IntoTheStockpile_WhenFoodIsShortAndInSeason()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            // The 56-loaves-per-founder placeholder (448 total) runs out in about 25 days for 9 people -
            // sooner than the ~30 days this test needs for wheat to grow and be harvested. Top up loaves
            // so this test isolates the farming mechanic from that separate, pre-existing balance gap.
            inventory.Add(storehouseId, "good.loaf", 10000);

            // With no construction site and no wheat yet, every founder's top priority is farming.
            for (long i = 0; i < CalendarConstants.TicksPerMonth + CalendarConstants.TicksPerDay * 5; i++)
            {
                sim.Step();
            }

            Assert.Greater(inventory.GetCount(storehouseId, "good.wheat"), 0,
                "Founders should have planted, grown and harvested wheat into the stockpile within about a month.");
        }

        [Test]
        public void Founders_FinishAWaitingCrownSite_WithUnpaidOrHiredLabor_WithinAReasonableTime()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, _, _, _) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.plank", 1000);
            inventory.Add(storehouseId, "good.stone", 1000);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(50, 50), out EntityId siteId, out string placeError), placeError);

            for (long i = 0; i < CalendarConstants.TicksPerDay * 15; i++)
            {
                sim.Step();
            }

            Assert.IsTrue(buildings.TryGetBuilding(siteId, out BuildingState site));
            Assert.IsNull(site.Construction, "The founders (building for free, or hired as paid builders) should finish the crown lodging within 15 days.");
        }

        [Test]
        public void JoblessNonFounder_SellsGatheredGoods_ToTheCrownAtMarketPrice()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, _, BuildingsModule buildings, InventoryModule inventory, WealthModule wealth, MarketModule market, PeopleModule people) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.wheat", 10000); // skip past farming priority straight to material gathering

            EntityId gatherer = people.SpawnPerson(30, 30);
            Assert.IsFalse(people.IsFounder(gatherer));

            for (long i = 0; i < CalendarConstants.TicksPerDay * 10; i++)
            {
                sim.Step();
            }

            Assert.Greater(inventory.GetCount(storehouseId, "good.log"), 0,
                "The crown should have bought at least one log from the jobless gatherer, landing it in the storehouse.");
            Assert.Greater(wealth.GetBalance(AccountId.Person(gatherer)).HundredthsOfShil, 0,
                "The gatherer should have been paid at the market price for what the crown bought.");
        }

        [Test]
        public void SaveAndLoad_MidGathering_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 555;
            long n = CalendarConstants.TicksPerDay * 4 + 123; // a few days into gathering
            long m = CalendarConstants.TicksPerDay * 3; // continue a few more days after loading

            (Simulation uninterrupted, _, BuildingsModule uninterruptedBuildings, _, _, _, _) = BuildWithSeed(core, seed);
            Assert.IsTrue(uninterruptedBuildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out _, out string error0), error0);
            for (long i = 0; i < n + m; i++)
            {
                uninterrupted.Step();
            }

            ulong expectedHash = uninterrupted.ComputeStateHash();

            (Simulation firstRun, _, BuildingsModule firstBuildings, _, _, _, _) = BuildWithSeed(core, seed);
            Assert.IsTrue(firstBuildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out _, out string error1), error1);
            for (long i = 0; i < n; i++)
            {
                firstRun.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstRun, new SaveHeaderInfo { KingdomName = "K", Population = 9 }, stream);
            stream.Position = 0;

            IModule[] secondModules = ModulesWithSeed(out _, out _, out _, out _, out _, out _, out _);
            (Simulation secondRun, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            for (long i = 0; i < m; i++)
            {
                secondRun.Step();
            }

            Assert.AreEqual(expectedHash, secondRun.ComputeStateHash());
        }

        private static IModule[] ModulesWithSeed(out MapMovementModule mapMovement, out InventoryModule inventory, out PeopleModule people, out WealthModule wealth, out BuildingsModule buildings, out MarketModule market, out GatheringModule gathering)
        {
            mapMovement = new MapMovementModule();
            inventory = new InventoryModule();
            people = new PeopleModule(mapMovement, inventory);
            wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering };
        }

        private static (Simulation Simulation, GatheringModule Gathering, BuildingsModule Buildings, InventoryModule Inventory, WealthModule Wealth, MarketModule Market, PeopleModule People) BuildWithSeed(TempCoreContentCopy core, ulong seed)
        {
            IModule[] modules = ModulesWithSeed(out MapMovementModule _, out InventoryModule inventory, out PeopleModule people, out WealthModule wealth, out BuildingsModule buildings, out MarketModule market, out GatheringModule gathering);
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, gathering, buildings, inventory, wealth, market, people);
        }
    }
}
