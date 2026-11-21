using NUnit.Framework;
using TheEmptying.Core;
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

namespace TheEmptying.Tests.EditMode.Buildings
{
    public class BuildingsModulePlacementTests
    {
        private static (Simulation Simulation, BuildingsModule Buildings, LandModule Land, WealthModule Wealth, InventoryModule Inventory) Build(TempCoreContentCopy core)
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
            return (simulation, buildings, land, wealth, inventory);
        }

        [Test]
        public void PrivateBuilding_OnlyPlacesOnOwnersPlot_InAnAllowedZone_FullyInside()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory) = Build(core);

            EntityId owner = sim.Entities.Create(EntityKind.Person);
            land.RequestSetZone(new CellRect(50, 50, 4, 4), "zone.residential_low");
            sim.Step();
            wealth.Grant(AccountId.Person(owner), Money.FromCopper(5000));
            Assert.IsTrue(land.TryBuyPlot(owner, "zone.residential_low", new GridPoint(50, 50), out _, out string buyError), buyError);

            Assert.IsTrue(buildingsModule.TryRequestPrivateBuilding(owner, "building.cottage", out EntityId buildingId, out string error), error);

            bool found = false;
            foreach (var row in buildingsModule.Buildings)
            {
                if (row.Key == buildingId)
                {
                    found = true;
                    Assert.AreEqual(owner, row.Value.Owner);
                    CellRect plotRect = new CellRect(50, 50, 4, 4);
                    foreach (GridPoint cell in row.Value.Rect.Cells())
                    {
                        Assert.IsTrue(plotRect.Contains(cell), "The building's footprint must lie fully inside the plot.");
                    }
                }
            }

            Assert.IsTrue(found);
        }

        [Test]
        public void PrivateBuilding_WithNoSuitablePlot_IsRefused()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory) = Build(core);

            EntityId person = sim.Entities.Create(EntityKind.Person);

            Assert.IsFalse(buildingsModule.TryRequestPrivateBuilding(person, "building.cottage", out _, out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void GovernmentBuilding_PlacesAnywhereFree_IncludingTheCrownGrounds()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory) = Build(core);

            // Somewhere far from the crown grounds (and the keep already built there).
            Assert.IsTrue(buildingsModule.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(10, 10), out EntityId id, out string error), error);

            bool found = false;
            foreach (var row in buildingsModule.Buildings)
            {
                if (row.Key == id)
                {
                    found = true;
                    Assert.IsNull(row.Value.Owner, "A government building has no owner.");
                }
            }

            Assert.IsTrue(found);
        }

        [Test]
        public void TheKeep_ExistsAtTheStartOfANewGame()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, _, _) = Build(core);

            bool found = false;
            foreach (var row in buildingsModule.Buildings)
            {
                if (row.Value.BuildingTypeId == "building.keep")
                {
                    found = true;
                    Assert.IsNull(row.Value.Construction, "The keep costs nothing, so it should already be finished.");
                    foreach (GridPoint cell in row.Value.Rect.Cells())
                    {
                        Assert.IsTrue(land.CrownGrounds.Contains(cell), "The keep must stand inside the crown grounds.");
                    }
                }
            }

            Assert.IsTrue(found, "A new game should start with the keep already built.");
        }

        [Test]
        public void OverlappingBuildings_AreRefused()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, BuildingsModule buildingsModule, LandModule land, WealthModule wealth, InventoryModule inventory) = Build(core);

            Assert.IsTrue(buildingsModule.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(10, 10), out _, out string firstError), firstError);
            Assert.IsFalse(buildingsModule.TryPlaceGovernmentBuilding("building.crown_lodging", new GridPoint(12, 12), out _, out string error), "An overlapping building must be refused.");
            Assert.IsNotNull(error);
        }
    }
}
