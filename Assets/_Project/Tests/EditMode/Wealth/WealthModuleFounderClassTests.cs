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
using TheEmptying.Modules.MapResources;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Wealth
{
    /// <summary>3.9b: founders stay lower class forever, the advisor is upper in name/power only (lower-class needs), the monarch is upper, and none of them ever change.</summary>
    public class WealthModuleFounderClassTests
    {
        private static (Simulation Simulation, PeopleModule People, WealthModule Wealth, LandModule Land, BuildingsModule Buildings, InventoryModule Inventory) Build(TempCoreContentCopy core)
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
            return (simulation, people, wealth, land, buildings, inventory);
        }

        [Test]
        public void PlainFounders_AreLowerClass_AdvisorIsUpper_MonarchIsUpper()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, WealthModule wealth, _, _, _) = Build(core);

            foreach (var row in people.People.Where(r => r.Value.IsFounder && !people.IsAdvisor(r.Key)))
            {
                Assert.AreEqual(SocialClass.Lower, wealth.GetClass(row.Key));
            }

            Assert.AreEqual(SocialClass.Upper, wealth.GetClass(people.AdvisorId.Value), "The advisor is upper class in name and power only.");
            Assert.AreEqual(SocialClass.Upper, wealth.GetClass(people.MonarchId.Value));
        }

        [Test]
        public void TheAdvisor_KeepsLowerClassNeeds_DespiteBeingUpperClass()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, WealthModule wealth, _, _, _) = Build(core);

            people.TryGetPerson(people.AdvisorId.Value, out PersonState advisor);
            Assert.AreEqual(SocialClass.Upper, wealth.GetClass(people.AdvisorId.Value));

            var upperOnlyNeedIds = sim.Content.ListByType(BuiltInContentTypes.NeedTypeName)
                .Select(e => (Need)e.Definition)
                .Where(n => n.Kind == NeedKind.Meter && n.MinimumClass == SocialClass.Upper)
                .Select(n => n.Id)
                .ToList();

            Assert.Greater(upperOnlyNeedIds.Count, 0, "Sanity check: there should be at least one upper-class-only need in the content.");

            foreach (string needId in upperOnlyNeedIds)
            {
                Assert.IsFalse(advisor.Needs.ContainsKey(needId), $"The advisor keeps lower-class needs, so should not have the upper-only need '{needId}'.");
            }
        }

        [Test]
        public void FoundersAndMonarch_NeverChangeClass_EvenAfterManyMonths()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, WealthModule wealth, _, BuildingsModule buildings, InventoryModule inventory) = Build(core);

            // Enough food for 9 people over 3 months: nothing replenishes the stockpile in this
            // isolated test (no production chain yet), so without this everyone eventually starves,
            // and a dead advisor's class falls back to the default rather than staying put.
            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);
            inventory.Add(storehouseId, "good.loaf", 10000);

            var beforeClasses = people.People.ToDictionary(r => r.Key, r => wealth.GetClass(r.Key));

            for (long i = 0; i < CalendarConstants.TicksPerMonth * 3; i++)
            {
                sim.Step();
            }

            foreach (var row in beforeClasses)
            {
                Assert.AreEqual(row.Value, wealth.GetClass(row.Key), $"{row.Key} should never change class.");
            }
        }

        [Test]
        public void Founders_CannotBuyAPlot()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, WealthModule wealth, LandModule land, _, _) = Build(core);

            EntityId founder = people.People.First(r => r.Value.IsFounder).Key;
            wealth.Grant(AccountId.Person(founder), Money.FromCopper(5000)); // even with money granted for the test

            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();

            Assert.IsFalse(land.TryBuyPlot(founder, "zone.residential_low", new GridPoint(50, 50), out _, out string error),
                "A founder should never be able to buy a plot.");
            Assert.IsNotNull(error);
        }

        [Test]
        public void Founders_CannotRequestAPrivateBuilding()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, PeopleModule people, WealthModule wealth, LandModule land, BuildingsModule buildings, InventoryModule inventory) = Build(core);

            EntityId founder = people.People.First(r => r.Value.IsFounder).Key;
            wealth.Grant(AccountId.Person(founder), Money.FromCopper(5000));

            Assert.IsFalse(buildings.TryRequestPrivateBuilding(founder, "building.cottage", out _, out string error),
                "A founder owns nothing and should never be able to request a private building.");
            Assert.IsNotNull(error);
        }
    }
}
