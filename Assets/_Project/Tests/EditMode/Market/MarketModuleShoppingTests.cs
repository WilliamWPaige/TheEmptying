using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
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

namespace TheEmptying.Tests.EditMode.Market
{
    /// <summary>Personal shopping: loaves when low on food, furniture for middle-class comfort, always from the nearest seller they can afford.</summary>
    public class MarketModuleShoppingTests
    {
        private static (Simulation Simulation, MarketModule Market, BuildingsModule Buildings, WealthModule Wealth, InventoryModule Inventory, PeopleModule People, MapMovementModule MapMovement) Build(TempCoreContentCopy core)
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
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            foreach (var row in people.People.ToList())
            {
                simulation.Entities.Destroy(row.Key);
            }

            return (simulation, market, buildings, wealth, inventory, people, mapMovement);
        }

        private static void StepDays(Simulation sim, int days)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay * days; i++)
            {
                sim.Step();
            }
        }

        [Test]
        public void APersonLowOnFood_BuysLoaves_FromTheNearestSeller()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, MapMovementModule mapMovement) = Build(core);

            // SpawnPerson's arguments are an age range, not a position - it lands on a random passable
            // cell - so the two sellers are placed relative to wherever the person actually spawned.
            // Below the adult age on purpose: an adult, jobless person here would get swept into a
            // hand-gathering task the moment FinishMarket's storehouse goes up (any jobless adult is
            // eligible), physically relocating them before the day is out and making "near"/"far"
            // meaningless by the time the purchase happens. A child has no such task to be swept into,
            // and hunger/shopping aren't gated by age, so it isolates exactly the behavior under test.
            EntityId person = people.SpawnPerson(10, 10);
            wealth.Grant(AccountId.Person(person), Money.FromCopper(1000));
            inventory.TryRemove(person, "good.loaf", inventory.GetCount(person, "good.loaf")); // start with none, below the 2-day target

            GridPoint personCell = mapMovement.GetCell(person);
            var nearAt = new GridPoint(Clamp(personCell.X + 6, 0, 250), Clamp(personCell.Y + 6, 0, 250));
            var farAt = new GridPoint(personCell.X < 128 ? 250 : 2, personCell.Y < 128 ? 250 : 2);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.market", farAt, out EntityId farCompleteShop, out string farErr), farErr);
            FinishMarket(buildings, inventory, farCompleteShop);
            inventory.Add(farCompleteShop, "good.loaf", 1000);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.market", nearAt, out EntityId nearShop, out string nearErr), nearErr);
            FinishMarket(buildings, inventory, nearShop);
            inventory.Add(nearShop, "good.loaf", 1000);

            int nearBefore = inventory.GetCount(nearShop, "good.loaf");
            int farBefore = inventory.GetCount(farCompleteShop, "good.loaf");

            StepDays(sim, 1);

            Assert.Greater(inventory.GetCount(person, "good.loaf"), 0, "A person low on food should have bought loaves.");
            Assert.Less(inventory.GetCount(nearShop, "good.loaf"), nearBefore, "The nearer seller should have been used.");
            Assert.AreEqual(farBefore, inventory.GetCount(farCompleteShop, "good.loaf"), "The farther seller should not have been touched while the nearer one had stock.");
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        [Test]
        public void AMiddleClassPersonLackingFurniture_BuysOne()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, MapMovementModule mapMovement) = Build(core);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.market", new GridPoint(10, 10), out EntityId shop, out string placeError), placeError);
            FinishMarket(buildings, inventory, shop);
            inventory.Add(shop, "good.furniture", 10);
            inventory.Add(shop, "good.loaf", 10000);

            EntityId person = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(person), Money.FromCopper(1000));
            inventory.Add(person, "good.loaf", 10000); // food isn't the thing under test
            wealth.SetJobTierProvider(entity => entity == person ? SocialClass.Middle : SocialClass.Lower);

            Assert.AreEqual(0, inventory.GetCount(person, "good.furniture"));
            StepDays(sim, 32); // the job tier only lifts their class at the next monthly check, not instantly
            Assert.AreEqual(1, inventory.GetCount(person, "good.furniture"));

            int furnitureAfterFirstMonth = inventory.GetCount(person, "good.furniture");
            StepDays(sim, 1);
            Assert.AreEqual(furnitureAfterFirstMonth, inventory.GetCount(person, "good.furniture"), "Already having one, they shouldn't buy another.");
        }

        [Test]
        public void ALowerClassPerson_DoesNotBuyFurniture()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MarketModule market, BuildingsModule buildings, WealthModule wealth, InventoryModule inventory, PeopleModule people, MapMovementModule mapMovement) = Build(core);

            Assert.IsTrue(buildings.TryPlaceGovernmentBuilding("building.market", new GridPoint(10, 10), out EntityId shop, out string placeError), placeError);
            FinishMarket(buildings, inventory, shop);
            inventory.Add(shop, "good.furniture", 10);
            inventory.Add(shop, "good.loaf", 10000);

            EntityId person = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(person), Money.FromCopper(1000));
            inventory.Add(person, "good.loaf", 10000);

            StepDays(sim, 3);

            Assert.AreEqual(0, inventory.GetCount(person, "good.furniture"));
        }

        /// <summary>Crown sites draw materials from the crown storehouse (3.9); ensures one complete, well-stocked storehouse exists (reusing it across calls), then finishes the given market site.</summary>
        private static void FinishMarket(BuildingsModule buildings, InventoryModule inventory, EntityId marketId)
        {
            if (!buildings.TryFindCrownStorehouse(out EntityId storehouseId))
            {
                Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(200, 200), out storehouseId, out string storehouseError), storehouseError);
                inventory.Add(storehouseId, "good.plank", 10000);
                inventory.Add(storehouseId, "good.stone", 10000);
            }

            buildings.TryDeliverGoods(marketId, "good.plank", 20, out string plankError);
            buildings.TryDeliverGoods(marketId, "good.stone", 10, out string stoneError);
            Assert.IsTrue(buildings.TryAddLaborHours(marketId, 9999, out string laborError), laborError);
        }
    }
}
