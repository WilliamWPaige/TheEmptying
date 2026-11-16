using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Buildings
{
    public class BuildingsModuleHomesTests
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
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            simulation.Settings.Set(PeopleModule.HomelessHealthDamagePerNightSettingId, 0); // isolate homes/rent from the unrelated homelessness-death mechanic

            // The 8 starting immigrants are homeless too, and would otherwise compete with
            // this test's own people for the same handful of household slots.
            foreach (var row in people.People.ToList())
            {
                simulation.Entities.Destroy(row.Key);
            }

            return (simulation, buildings, land, wealth, people, inventory);
        }

        private static void StepDays(Simulation sim, int days)
        {
            for (long i = 0; i < CalendarConstants.TicksPerDay * days; i++)
            {
                sim.Step();
            }
        }

        private static void StepMonths(Simulation sim, int months)
        {
            for (long i = 0; i < CalendarConstants.TicksPerMonth * months; i++)
            {
                sim.Step();
            }
        }

        private static EntityId BuildAndFinishCrownLodging(BuildingsModule buildingsModule, GridPoint at)
        {
            Assert.IsTrue(buildingsModule.TryPlaceGovernmentBuilding("building.crown_lodging", at, out EntityId id, out string error), error);
            buildingsModule.TryDeliverGoods(id, "good.plank", 60, out _);
            buildingsModule.TryDeliverGoods(id, "good.stone", 20, out _);
            buildingsModule.TryAddLaborHours(id, 80, out _);
            return id;
        }

        private static EntityId BuildAndFinishCottage(Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory, EntityId owner, GridPoint plotAt)
        {
            land.RequestSetZone(new CellRect(plotAt.X, plotAt.Y, 4, 4), "zone.residential_low");
            sim.Step(); // zone requests apply on the next tick
            wealth.Grant(AccountId.Person(owner), Money.FromShil(5000));
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", plotAt, out _, out string buyError), buyError);
            Assert.IsTrue(buildingsModule.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId cottageId, out string requestError), requestError);
            inventory.Add(owner, "good.plank", 40);
            inventory.Add(owner, "good.stone", 10);
            Assert.IsTrue(buildingsModule.TryDeliverGoods(cottageId, "good.plank", 40, out string plankError), plankError);
            Assert.IsTrue(buildingsModule.TryDeliverGoods(cottageId, "good.stone", 10, out string stoneError), stoneError);
            Assert.IsTrue(buildingsModule.TryAddLaborHours(cottageId, 50, out string laborError), laborError);
            return cottageId;
        }

        [Test]
        public void AHomelessPerson_RentsAnAffordableVacantHome_AndSleepsThere()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, _, _, PeopleModule people, InventoryModule inventory) = Build(core);

            BuildAndFinishCrownLodging(buildingsModule, new GridPoint(10, 10));
            EntityId person = people.SpawnPerson(30, 30);
            inventory.Add(person, "good.loaf", 10000); // food isn't the thing under test

            Assert.IsTrue(people.IsHomeless(person));

            StepDays(sim, 1);

            Assert.IsFalse(people.IsHomeless(person), "A homeless person should find a vacant, affordable home within a day.");

            people.TryGetPerson(person, out PersonState state);
            state.Needs["need.rest"].Meter = 0f; // give them an immediate reason to sleep
            StepDays(sim, 1);

            Assert.AreEqual(PersonActivity.Sleeping, state.Activity);
        }

        [Test]
        public void Rent_IsPaidMonthlyToTheOwner()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            BuildAndFinishCottage(sim, buildingsModule, land, wealth, inventory, owner, new GridPoint(40, 40)); // the owner's own home
            BuildAndFinishCottage(sim, buildingsModule, land, wealth, inventory, owner, new GridPoint(50, 50)); // a second cottage, built purely as a rental

            EntityId tenant = people.SpawnPerson(30, 30);
            inventory.Add(tenant, "good.loaf", 10000);
            wealth.Grant(AccountId.Person(tenant), Money.FromShil(1000));

            StepDays(sim, 1); // finds and rents the cottage, paying the first month up front
            Assert.IsFalse(people.IsHomeless(tenant));

            Money ownerBalanceBeforeSecondMonth = wealth.GetBalance(AccountId.Person(owner));
            StepMonths(sim, 1);

            Assert.Greater(wealth.GetBalance(AccountId.Person(owner)).HundredthsOfShil, ownerBalanceBeforeSecondMonth.HundredthsOfShil, "The owner should have received a second month's rent.");
        }

        [Test]
        public void UnpaidRent_Evicts()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            BuildAndFinishCottage(sim, buildingsModule, land, wealth, inventory, owner, new GridPoint(40, 40)); // the owner's own home
            BuildAndFinishCottage(sim, buildingsModule, land, wealth, inventory, owner, new GridPoint(50, 50)); // a second cottage, built purely as a rental

            EntityId tenant = people.SpawnPerson(30, 30);
            inventory.Add(tenant, "good.loaf", 10000);
            wealth.Grant(AccountId.Person(tenant), Money.FromShil(140)); // exactly one month's rent, nothing left over

            StepDays(sim, 1); // finds and rents the cottage (pays the first month up front)
            Assert.IsFalse(people.IsHomeless(tenant));

            StepMonths(sim, 1); // the tenant can't afford the next month's rent

            Assert.IsTrue(people.IsHomeless(tenant), "Unpaid rent should evict the tenant.");
        }

        [Test]
        public void CrownLodgingRent_IsPaidByTheCrown_NeverEvicting()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, _, WealthModule wealth, PeopleModule people, InventoryModule inventory) = Build(core);

            BuildAndFinishCrownLodging(buildingsModule, new GridPoint(10, 10));

            EntityId tenant = people.SpawnPerson(30, 30);
            inventory.Add(tenant, "good.loaf", 10000);
            // Deliberately no money: if rent were charged to them, they'd be evicted immediately.

            StepDays(sim, 1);
            Assert.IsFalse(people.IsHomeless(tenant));

            StepMonths(sim, 2);

            Assert.IsFalse(people.IsHomeless(tenant), "Crown lodging rent is paid by the crown, so a penniless tenant is never evicted.");
        }
    }
}
