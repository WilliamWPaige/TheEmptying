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

namespace TheEmptying.Tests.EditMode.Jobs
{
    public class JobsModuleSaveTests
    {
        private static IModule[] Modules(out JobsModule jobs, out BuildingsModule buildings, out WealthModule wealth, out LandModule land, out PeopleModule people, out InventoryModule inventory)
        {
            var mapMovement = new MapMovementModule();
            inventory = new InventoryModule();
            people = new PeopleModule(mapMovement, inventory);
            wealth = new WealthModule(people);
            land = new LandModule(mapMovement, wealth);
            buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            jobs = new JobsModule(buildings, wealth, people, mapMovement);
            var market = new MarketModule(buildings, wealth, inventory, people, mapMovement);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement);
            return new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering };
        }

        /// <summary>A private bakery with a hired skilled worker, and a government construction site with a hired builder, so every piece of Jobs' state (slots, pay counters, skill progress) has something in it.</summary>
        private static void PopulateJobsState(Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory)
        {
            EntityId owner = sim.Entities.Create(EntityKind.Person);
            land.RequestSetZone(new CellRect(50, 50, 6, 6), "zone.commercial");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromShil(10000));
            land.TryBuyPlot(owner, "zone.commercial", new GridPoint(50, 50), out _, out _);

            EntityId bakeryId;
            if (!buildings.TryRequestPrivateBuilding(owner, "building.bakery", out bakeryId, out _))
            {
                // Undiscovered: answering "Allow" makes BuildingsModule retry the original request internally,
                // so we must not call TryRequestPrivateBuilding again - the plot would already have a building on it.
                long questionId = sim.RuleRunner.PendingQuestions[0].QuestionId;
                sim.AnswerQuestion(questionId, 1); // Allow
                sim.Step();

                foreach (var row in buildings.Buildings)
                {
                    if (row.Value.Owner == owner && row.Value.BuildingTypeId == "building.bakery")
                    {
                        bakeryId = row.Key;
                        break;
                    }
                }
            }

            inventory.Add(owner, "good.plank", 20);
            inventory.Add(owner, "good.stone", 10);
            buildings.TryDeliverGoods(bakeryId, "good.plank", 20, out _);
            buildings.TryDeliverGoods(bakeryId, "good.stone", 10, out _);
            buildings.TryAddLaborHours(bakeryId, 30, out _);

            // A second storehouse needs a first, already-complete one to draw its own construction materials from.
            buildings.TryPlaceInitialStockpile(new GridPoint(90, 90), out EntityId fundingStorehouseId, out _);
            inventory.Add(fundingStorehouseId, "good.plank", 1000);
            inventory.Add(fundingStorehouseId, "good.stone", 1000);

            buildings.TryPlaceGovernmentBuilding("building.crown_storehouse", new GridPoint(10, 10), out EntityId storehouseId, out _);
            buildings.TryDeliverGoods(storehouseId, "good.plank", 20, out _);
            buildings.TryDeliverGoods(storehouseId, "good.stone", 10, out _); // goods delivered, no labor yet: stays a construction site

            EntityId worker = people.SpawnPerson(30, 30);
            inventory.Add(worker, "good.loaf", 10000);

            EntityId builder = people.SpawnPerson(60, 60);
            inventory.Add(builder, "good.loaf", 10000);
        }

        [Test]
        public void SaveAndLoad_MidShift_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 91;
            long n = CalendarConstants.TicksPerDay * 3; // past hiring and into an ongoing shift
            long m = CalendarConstants.TicksPerMonth; // past several pay cycles and skill gains

            IModule[] uninterruptedModules = Modules(out JobsModule _, out BuildingsModule uninterruptedBuildings, out WealthModule uninterruptedWealth, out LandModule uninterruptedLand, out PeopleModule uninterruptedPeople, out InventoryModule uninterruptedInventory);
            (Simulation uninterruptedSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, uninterruptedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            PopulateJobsState(uninterruptedSim, uninterruptedBuildings, uninterruptedLand, uninterruptedWealth, uninterruptedPeople, uninterruptedInventory);

            for (long i = 0; i < n + m; i++)
            {
                uninterruptedSim.Step();
            }

            ulong expectedHash = uninterruptedSim.ComputeStateHash();

            IModule[] firstModules = Modules(out JobsModule _, out BuildingsModule firstBuildings, out WealthModule firstWealth, out LandModule firstLand, out PeopleModule firstPeople, out InventoryModule firstInventory);
            (Simulation firstSim, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, firstModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            PopulateJobsState(firstSim, firstBuildings, firstLand, firstWealth, firstPeople, firstInventory);

            for (long i = 0; i < n; i++)
            {
                firstSim.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(firstSim, new SaveHeaderInfo { KingdomName = "K", Population = 2 }, stream);
            stream.Position = 0;

            IModule[] secondModules = Modules(out JobsModule _, out BuildingsModule _, out WealthModule _, out LandModule _, out PeopleModule _, out InventoryModule _);
            (Simulation secondSim, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (long i = 0; i < m; i++)
            {
                secondSim.Step();
            }

            Assert.AreEqual(expectedHash, secondSim.ComputeStateHash());
        }
    }
}
