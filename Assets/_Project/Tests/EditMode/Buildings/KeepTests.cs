using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
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
    /// <summary>3.9i: the keep is pre-built, complete, from the first tick - never placeable, free, housing founders and the monarch immediately.</summary>
    public class KeepTests
    {
        private static (Simulation Simulation, BuildingsModule Buildings, PeopleModule People, InventoryModule Inventory) Build(TempCoreContentCopy core)
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
            return (simulation, buildings, people, inventory);
        }

        [Test]
        public void NewGame_HasACompleteKeep_InTheCrownGrounds_HousingFoundersAndMonarchFromTickOne()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, PeopleModule people, _) = Build(core);

            EntityId keepId = EntityId.None;
            BuildingState keepBuilding = null;
            foreach (KeyValuePair<EntityId, BuildingState> row in buildings.Buildings)
            {
                if (row.Value.BuildingTypeId == BuildingsModule.KeepTypeId)
                {
                    keepId = row.Key;
                    keepBuilding = row.Value;
                    break;
                }
            }

            Assert.IsFalse(keepId.IsNone, "Expected a keep to exist from the start of a new game.");
            Assert.IsNull(keepBuilding.Construction, "Expected the keep to already be complete, never a construction site.");
            Assert.IsTrue(buildings.IsKeepStanding);

            // Housed from tick one - no stepping at all.
            foreach (KeyValuePair<EntityId, PersonState> row in people.People)
            {
                if (people.IsFounder(row.Key) || people.IsMonarch(row.Key))
                {
                    Assert.IsFalse(people.IsHomeless(row.Key), $"{row.Key} should already live in the keep.");
                    Assert.IsTrue(keepBuilding.Households.Any(h => h.Occupant == row.Key), $"{row.Key} should be a keep occupant.");
                }
            }
        }

        [Test]
        public void NewGame_StockpileStartingStock_IsUntouchedByTheKeep()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, PeopleModule people, InventoryModule inventory) = Build(core);

            Assert.IsTrue(buildings.TryPlaceInitialStockpile(new GridPoint(10, 10), out EntityId storehouseId, out string error), error);

            int expectedPlanks = (int)sim.Settings.Get(BuildingsModule.InitialStockpilePlanksSettingId);
            int expectedStone = (int)sim.Settings.Get(BuildingsModule.InitialStockpileStoneSettingId);

            Assert.AreEqual(expectedPlanks, inventory.GetCount(storehouseId, "good.plank"), "The keep should have drawn no planks from the stockpile.");
            Assert.AreEqual(expectedStone, inventory.GetCount(storehouseId, "good.stone"), "The keep should have drawn no stone from the stockpile.");
        }

        [Test]
        public void TheKeep_CanNeverBePlaced_ThroughTheGeneralGovernmentBuildingPath()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildings, PeopleModule people, InventoryModule inventory) = Build(core);

            bool placed = buildings.TryPlaceGovernmentBuilding(BuildingsModule.KeepTypeId, new GridPoint(50, 50), out _, out string error);

            Assert.IsFalse(placed, "Expected placing another keep to be refused.");
            Assert.IsNotNull(error);
        }
    }
}
