using System.IO;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
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

namespace TheEmptying.Tests.EditMode.Buildings
{
    public class BuildingsModuleSaveTests
    {
        private static IModule[] Modules(out BuildingsModule buildings, out WealthModule wealth, out LandModule land, out PeopleModule people, out InventoryModule inventory)
        {
            var mapMovement = new MapMovementModule();
            inventory = new InventoryModule();
            people = new PeopleModule(mapMovement, inventory);
            wealth = new WealthModule(people);
            land = new LandModule(mapMovement, wealth);
            buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering };
        }

        /// <summary>A government site mid-construction, a finished private cottage, and a tenant, so every piece of Buildings' state has something in it.</summary>
        private static void PopulateBuildingsState(Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory)
        {
            buildings.TryPlaceInitialStockpile(new GridPoint(90, 90), out EntityId storehouseId, out _);
            inventory.Add(storehouseId, "good.plank", 1000);

            buildings.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(10, 10), out _, out _);
            buildings.TryDeliverGoods(FindSiteByType(buildings, "building.crown_lodging"), "good.plank", 30, out _); // half-delivered, mid-construction, drawn from the storehouse

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(5000));
            land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out _, out _);
            buildings.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out _);
            buildings.TryDeliverGoods(cottageId, "good.plank", 40, out _);
            buildings.TryDeliverGoods(cottageId, "good.stone", 10, out _);
            buildings.TryAddLaborHours(cottageId, 50, out _);

            EntityId tenant = people.SpawnPerson(30, 30);
            inventory.Add(tenant, "good.loaf", 10000);
            wealth.Grant(AccountId.Person(tenant), Money.FromCopper(1000));
        }

        private static EntityId FindSiteByType(BuildingsModule buildings, string typeId)
        {
            foreach (var row in buildings.Buildings)
            {
                if (row.Value.BuildingTypeId == typeId)
                {
                    return row.Key;
                }
            }

            return EntityId.None;
        }

        [Test]
        public void SaveAndLoad_MidConstruction_WithTenants_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 77;
            long n = CalendarConstants.TicksPerDay * 3; // past the daily home search
            long m = CalendarConstants.TicksPerMonth; // past a monthly rent/tax charge

            IModule[] uninterruptedModules = Modules(out BuildingsModule uninterruptedBuildings, out WealthModule uninterruptedWealth, out LandModule uninterruptedLand, out PeopleModule uninterruptedPeople, out InventoryModule uninterruptedInventory);
            (Simulation uninterruptedSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, uninterruptedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            PopulateBuildingsState(uninterruptedSim, uninterruptedBuildings, uninterruptedLand, uninterruptedWealth, uninterruptedPeople, uninterruptedInventory);

            for (long i = 0; i < n + m; i++)
            {
                uninterruptedSim.Step();
            }

            ulong expectedHash = uninterruptedSim.ComputeStateHash();

            IModule[] firstModules = Modules(out BuildingsModule firstBuildings, out WealthModule firstWealth, out LandModule firstLand, out PeopleModule firstPeople, out InventoryModule firstInventory);
            (Simulation firstSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, firstModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            PopulateBuildingsState(firstSim, firstBuildings, firstLand, firstWealth, firstPeople, firstInventory);

            for (long i = 0; i < n; i++)
            {
                firstSim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 2 }, stream);
            stream.Position = 0;

            IModule[] secondModules = Modules(out BuildingsModule _, out WealthModule _, out LandModule _, out PeopleModule _, out InventoryModule _);
            (Simulation secondSim, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (long i = 0; i < m; i++)
            {
                secondSim.Step();
            }

            Assert.AreEqual(expectedHash, secondSim.ComputeStateHash());
        }
    }
}
