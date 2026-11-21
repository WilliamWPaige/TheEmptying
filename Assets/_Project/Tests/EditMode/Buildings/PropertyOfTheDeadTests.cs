using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
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

namespace TheEmptying.Tests.EditMode.Buildings
{
    /// <summary>Property of the dead: plots, buildings, money and goods all return to the crown; existing tenants stay, now renting from the crown.</summary>
    public class PropertyOfTheDeadTests
    {
        private static (Simulation Simulation, BuildingsModule Buildings, LandModule Land, WealthModule Wealth, PeopleModule People, InventoryModule Inventory) Build(TempCoreContentCopy core)
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

            return (simulation, buildings, land, wealth, people, inventory);
        }

        private static EntityId BuildAndFinishCottage(Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, InventoryModule inventory, EntityId owner, GridPoint plotAt)
        {
            land.RequestSetZone(new CellRect(plotAt.X, plotAt.Y, 4, 4), "zone.residential_low");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(5000));
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", plotAt, out _, out string buyError), buyError);
            Assert.IsTrue(buildings.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string requestError), requestError);
            inventory.Add(owner, "good.plank", 40);
            inventory.Add(owner, "good.stone", 10);
            Assert.IsTrue(buildings.TryDeliverGoods(cottageId, "good.plank", 40, out string plankError), plankError);
            Assert.IsTrue(buildings.TryDeliverGoods(cottageId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildings.TryAddLaborHours(cottageId, 50, out string laborError), laborError);
            return cottageId;
        }

        [Test]
        public void ABuildingTheDeadPersonOwned_ReturnsToTheCrown_AndItsTenantStays()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = people.SpawnPerson(30, 30);
            inventory.Add(owner, "good.loaf", 10000);
            EntityId ownHome = BuildAndFinishCottage(sim, buildings, land, wealth, inventory, owner, new GridPoint(40, 40)); // owner moves in here (homeless at the time)
            EntityId rental = BuildAndFinishCottage(sim, buildings, land, wealth, inventory, owner, new GridPoint(60, 60)); // a second cottage, purely a rental

            EntityId tenant = people.SpawnPerson(30, 30);
            inventory.Add(tenant, "good.loaf", 10000);
            wealth.Grant(AccountId.Person(tenant), Money.FromCopper(1000));

            for (long i = 0; i < TheEmptying.Core.Timekeeping.CalendarConstants.TicksPerDay; i++)
            {
                sim.Step(); // the tenant finds and rents the vacant rental cottage
            }

            Assert.IsTrue(buildings.TryGetBuilding(rental, out BuildingState rentalBefore));
            Assert.AreEqual(tenant, rentalBefore.Households[0].Occupant);

            sim.Commands.Execute($"kill {owner}");
            sim.Step(); // the person-died event, posted outside the tick loop, only delivers on the next tick

            Assert.IsTrue(buildings.TryGetBuilding(ownHome, out BuildingState ownHomeAfter));
            Assert.IsFalse(ownHomeAfter.Owner.HasValue, "The owner's own home should return to the crown.");
            Assert.IsFalse(ownHomeAfter.Households[0].Occupant.HasValue, "The dead owner's own household slot should be vacated.");

            Assert.IsTrue(buildings.TryGetBuilding(rental, out BuildingState rentalAfter));
            Assert.IsFalse(rentalAfter.Owner.HasValue, "The rental property should also return to the crown.");
            Assert.AreEqual(tenant, rentalAfter.Households[0].Occupant, "The tenant should stay, now renting from the crown.");
        }

        [Test]
        public void TheDeadOwnersTenant_StillPaysRent_NowToTheCrown()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = people.SpawnPerson(30, 30);
            inventory.Add(owner, "good.loaf", 10000);
            BuildAndFinishCottage(sim, buildings, land, wealth, inventory, owner, new GridPoint(40, 40));
            EntityId rental = BuildAndFinishCottage(sim, buildings, land, wealth, inventory, owner, new GridPoint(60, 60));

            EntityId tenant = people.SpawnPerson(30, 30);
            inventory.Add(tenant, "good.loaf", 10000);
            wealth.Grant(AccountId.Person(tenant), Money.FromCopper(2000));

            for (long i = 0; i < TheEmptying.Core.Timekeeping.CalendarConstants.TicksPerDay; i++)
            {
                sim.Step();
            }

            sim.Commands.Execute($"kill {owner}");

            Money crownBefore = wealth.GetBalance(AccountId.Crown);
            Money tenantBefore = wealth.GetBalance(AccountId.Person(tenant));

            for (long i = 0; i < TheEmptying.Core.Timekeeping.CalendarConstants.TicksPerMonth; i++)
            {
                sim.Step();
            }

            Assert.Greater(wealth.GetBalance(AccountId.Crown).HundredthsOfCopper, crownBefore.HundredthsOfCopper, "The crown should have collected the rent the dead owner used to receive.");
            Assert.Less(wealth.GetBalance(AccountId.Person(tenant)).HundredthsOfCopper, tenantBefore.HundredthsOfCopper);
            Assert.IsTrue(buildings.TryGetBuilding(rental, out BuildingState rentalAfter));
            Assert.AreEqual(tenant, rentalAfter.Households[0].Occupant, "Paying the rent should keep the tenant housed.");
        }

        [Test]
        public void ADeadPersonsMoneyAndGoods_GoToTheCrown()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string placeError), placeError);

            EntityId person = people.SpawnPerson(30, 30); // starts with some automatic food provisions
            wealth.Grant(AccountId.Person(person), Money.FromCopper(500));
            inventory.Add(person, "good.loaf", 42);

            Money crownBefore = wealth.GetBalance(AccountId.Crown);
            int storehouseLoavesBefore = inventory.GetCount(storehouseId, "good.loaf");
            int personLoavesBeforeDeath = inventory.GetCount(person, "good.loaf");

            sim.Commands.Execute($"kill {person}");
            sim.Step();

            Assert.AreEqual(crownBefore + Money.FromCopper(500), wealth.GetBalance(AccountId.Crown));
            Assert.AreEqual(storehouseLoavesBefore + personLoavesBeforeDeath, inventory.GetCount(storehouseId, "good.loaf"));
            Assert.AreEqual(0, inventory.GetCount(person, "good.loaf"));
        }

        [Test]
        public void ADeadPersonsPlots_ReturnToTheCrown_OwnedOrLeased()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = people.SpawnPerson(30, 30);
            land.RequestSetZone(new CellRect(40, 40, 4, 4), "zone.residential_low");
            land.RequestSetZone(new CellRect(80, 80, 4, 4), "zone.residential_low");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(10000));
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(40, 40), out EntityId ownedPlotId, out string buyError), buyError);

            EntityId leaseholder = people.SpawnPerson(30, 30);
            wealth.Grant(AccountId.Person(leaseholder), Money.FromCopper(10000));
            Assert.IsTrue(land.TryLeasePlot(leaseholder, "zone.residential_low", new GridPoint(80, 80), out EntityId leasedPlotId, out string leaseError), leaseError);

            sim.Commands.Execute($"kill {owner}");
            sim.Commands.Execute($"kill {leaseholder}");
            sim.Step();

            PlotState ownedPlot = land.Plots.First(p => p.Key == ownedPlotId).Value;
            PlotState leasedPlot = land.Plots.First(p => p.Key == leasedPlotId).Value;
            Assert.IsFalse(ownedPlot.Owner.HasValue, "A dead owner's plot should return to the crown.");
            Assert.IsFalse(leasedPlot.Leaseholder.HasValue, "A dead leaseholder's lease should simply end.");
        }
    }
}
