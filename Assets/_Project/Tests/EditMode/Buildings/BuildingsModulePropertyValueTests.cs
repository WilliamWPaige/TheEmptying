using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Buildings
{
    public class BuildingsModulePropertyValueTests
    {
        private static (Simulation Simulation, BuildingsModule Buildings, LandModule Land, WealthModule Wealth, InventoryModule Inventory) Build(TempCoreContentCopy core)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(mapMovement, wealth);
            var buildings = new BuildingsModule(mapMovement, land, wealth, inventory, people);
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, buildings, land, wealth, inventory);
        }

        [Test]
        public void AFinishedBuilding_AddsItsFullConstructionValue()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromShil(5000));
            land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out _, out _);
            inventory.Add(owner, "good.plank", 40);
            inventory.Add(owner, "good.stone", 10);

            Money wealthBeforeBuilding = wealth.GetWealth(owner);

            buildingsModule.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string error);
            Assert.IsNull(error);
            Assert.IsTrue(buildingsModule.TryDeliverGoods(cottageId, "good.plank", 40, out string plankError), plankError);
            Assert.IsTrue(buildingsModule.TryDeliverGoods(cottageId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildingsModule.TryAddLaborHours(cottageId, 50, out string laborError), laborError);

            // Cottage: 40 plank (2 shil each) + 10 stone (2.5 shil each) = 80 + 25 = 105 shil.
            // Delivering the goods removed them from the owner's inventory but that inventory has no wealth
            // value of its own here, so the only change to total wealth is the construction value appearing.
            Assert.AreEqual(wealthBeforeBuilding + Money.FromShil(105), wealth.GetWealth(owner));
        }

        [Test]
        public void APartlyBuiltSite_AddsOnlyTheValueDeliveredSoFar()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromShil(5000));
            land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out _, out _);
            inventory.Add(owner, "good.plank", 20);

            Money wealthBeforeBuilding = wealth.GetWealth(owner);

            buildingsModule.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out _);
            Assert.IsTrue(buildingsModule.TryDeliverGoods(cottageId, "good.plank", 20, out string plankError), plankError); // half the planks, no stone, no labor yet

            // 20 plank at 2 shil each = 40 shil of value delivered so far; the undelivered stone counts for nothing.
            Assert.AreEqual(wealthBeforeBuilding + Money.FromShil(40), wealth.GetWealth(owner));
        }
    }
}
