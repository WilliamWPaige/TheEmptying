using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
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

namespace TheEmptying.Tests.EditMode.Wealth
{
    public class WealthModuleStartingMoneyTests
    {
        private static (Simulation Simulation, WealthModule Wealth, PeopleModule People) Build(TempCoreContentCopy core, ulong seed)
        {
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealthModule = new WealthModule(people);
            var land = new LandModule(mapMovement, wealthModule);
            var buildings = new BuildingsModule(mapMovement, land, wealthModule, inventory, people);
            var jobs = new JobsModule(buildings, wealthModule, people, mapMovement);
            var market = new MarketModule(buildings, wealthModule, inventory, people, mapMovement);
            var mapResources = new MapResourcesModule(mapMovement, land);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, mapMovement, mapResources);
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealthModule, land, buildings, jobs, market, mapResources, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, wealthModule, people);
        }

        [Test]
        public void CrownAndImmigrants_StartInTheRightRanges()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, WealthModule wealth, PeopleModule people) = Build(core, 1);

            Assert.AreEqual(Money.FromCopper(5000), wealth.GetBalance(AccountId.Crown));

            foreach (KeyValuePair<EntityId, PersonState> row in people.People)
            {
                Money balance = wealth.GetBalance(AccountId.Person(row.Key));
                Assert.GreaterOrEqual(balance.HundredthsOfCopper, 0);
                Assert.LessOrEqual(balance.HundredthsOfCopper, Money.FromCopper(300).HundredthsOfCopper);

                // 3.9b: the advisor and the monarch are upper class (power/no job respectively); every
                // other founder, like a plain immigrant, is lower.
                SocialClass expectedClass = people.IsAdvisor(row.Key) || people.IsMonarch(row.Key) ? SocialClass.Upper : SocialClass.Lower;
                Assert.AreEqual(expectedClass, wealth.GetClass(row.Key));
            }
        }

        [Test]
        public void SameSeed_GivesIdenticalStartingBalances()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();
            (Simulation simA, WealthModule wealthA, PeopleModule peopleA) = Build(coreA, 777);
            (Simulation simB, WealthModule wealthB, PeopleModule peopleB) = Build(coreB, 777);

            List<long> balancesA = peopleA.People.Select(r => wealthA.GetBalance(AccountId.Person(r.Key)).HundredthsOfCopper).OrderBy(b => b).ToList();
            List<long> balancesB = peopleB.People.Select(r => wealthB.GetBalance(AccountId.Person(r.Key)).HundredthsOfCopper).OrderBy(b => b).ToList();

            Assert.AreEqual(wealthA.GetBalance(AccountId.Crown), wealthB.GetBalance(AccountId.Crown));
            CollectionAssert.AreEqual(balancesA, balancesB);
        }
    }
}
