using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>R11: placing a building blocks its footprint except its one door cell, IsFounded reflects whether any building exists, and a placed building survives a save and load round trip.</summary>
    public class BuildingsModuleTests
    {
        private static (Simulation Simulation, MapModule Map, BuildingsModule Buildings) BuildNewGame(TempCoreContentCopy core, ulong seed = 1)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, (MapModule)modules.First(m => m is MapModule), (BuildingsModule)modules.First(m => m is BuildingsModule));
        }

        [Test]
        public void NewGame_IsNotFounded_AndHasNoBuildings()
        {
            using var core = new TempCoreContentCopy();
            (_, _, BuildingsModule buildings) = BuildNewGame(core);

            Assert.IsFalse(buildings.IsFounded);
            Assert.AreEqual(0, buildings.Buildings.Count());
        }

        [Test]
        public void PlaceBuilding_BlocksEveryFootprintCellExceptTheDoor()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, BuildingsModule buildings) = BuildNewGame(core);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            EntityId id = buildings.PlaceBuilding(BuildingsModule.KeepContentId, centerX, centerZ, 12, 12);

            Assert.IsTrue(buildings.IsFounded);
            Assert.IsTrue(buildings.TryGetBuilding(id, out BuildingState building));

            for (int dx = 0; dx < building.FootprintWidth; dx++)
            {
                for (int dz = 0; dz < building.FootprintDepth; dz++)
                {
                    var cell = new CellCoord(building.MinCell.X + dx, building.MinCell.Z + dz);
                    bool isDoor = cell == building.DoorCell;
                    Assert.AreEqual(!isDoor, buildings.IsCellBlocked(cell), $"Cell {cell} (door={isDoor}) blocked state is wrong.");
                }
            }

            // Just outside the footprint must never be blocked by this building.
            var justOutside = new CellCoord(building.MinCell.X - 1, building.MinCell.Z);
            Assert.IsFalse(buildings.IsCellBlocked(justOutside));
        }

        [Test]
        public void DoorCell_IsOnTheSouthEdge_InTheMiddle()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, BuildingsModule buildings) = BuildNewGame(core);

            EntityId id = buildings.PlaceBuilding(BuildingsModule.KeepContentId, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 12, 12);
            buildings.TryGetBuilding(id, out BuildingState building);

            Assert.AreEqual(building.MinCell.Z, building.DoorCell.Z, "The door should be on the south (lowest Z) edge.");
            Assert.AreEqual(building.MinCell.X + building.FootprintWidth / 2, building.DoorCell.X, "The door should be in the middle of that edge.");
        }

        [Test]
        public void SaveAndLoad_APlacedBuilding_SurvivesTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, BuildingsModule buildings) = BuildNewGame(core);

            EntityId id = buildings.PlaceBuilding(BuildingsModule.KeepContentId, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 12, 12);
            buildings.TryGetBuilding(id, out BuildingState before);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var loadedBuildings = (BuildingsModule)loadedModules.First(m => m is BuildingsModule);

            Assert.IsTrue(loadedBuildings.IsFounded);
            Assert.IsTrue(loadedBuildings.TryGetBuilding(id, out BuildingState after));

            Assert.AreEqual(before.ContentId, after.ContentId);
            Assert.AreEqual(before.WorldX, after.WorldX);
            Assert.AreEqual(before.WorldZ, after.WorldZ);
            Assert.AreEqual(before.FootprintWidth, after.FootprintWidth);
            Assert.AreEqual(before.FootprintDepth, after.FootprintDepth);
            Assert.AreEqual(before.MinCell, after.MinCell);
            Assert.AreEqual(before.DoorCell, after.DoorCell);
        }
    }
}
