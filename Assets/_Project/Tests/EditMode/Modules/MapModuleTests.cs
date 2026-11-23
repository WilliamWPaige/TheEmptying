using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation;
using TheEmptying.Presentation.World;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>
    /// R6: the shipped map settings (map_settings.json) load with the
    /// right defaults, and MapModule's world/cell/chunk conversions are
    /// correct at the center, the edges and the corners. R10: a chunk
    /// already created before a save keeps its exact placements after a
    /// load - never regenerated against its own (by then advanced)
    /// random stream, which would both scramble them and silently shift
    /// every other stream drawn from afterward.
    /// </summary>
    public class MapModuleTests
    {
        private static MapModule BuildFromRealContent()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);
            return (MapModule)modules.First(m => m is MapModule);
        }

        [TestCase(MapModule.WorldWidthSettingId, SettingValueType.Number, 10240.0, 256.0, 100000.0, "Map")]
        [TestCase(MapModule.WorldDepthSettingId, SettingValueType.Number, 10240.0, 256.0, 100000.0, "Map")]
        [TestCase(MapModule.CellSizeSettingId, SettingValueType.Number, 1.0, 0.1, 100.0, "Map")]
        [TestCase(MapModule.ChunkSizeSettingId, SettingValueType.Number, 256.0, 8.0, 4096.0, "Map")]
        [TestCase(WorldMapView.ViewDistanceChunksSettingId, SettingValueType.Integer, 3.0, 1.0, 6.0, "Map")]
        [TestCase(WorldMapView.LowDetailHeightSettingId, SettingValueType.Number, 400.0, 10.0, 5000.0, "Map")]
        [TestCase(MapModule.TreesPerChunkSettingId, SettingValueType.Integer, 20.0, 0.0, 500.0, "Map")]
        [TestCase(MapModule.StonesPerChunkSettingId, SettingValueType.Integer, 3.0, 0.0, 500.0, "Map")]
        [TestCase(MapModule.GrubMoundsPerChunkSettingId, SettingValueType.Integer, 2.0, 0.0, 500.0, "Map")]
        public void Setting_LoadsFromRealContent_WithExpectedDefaultRangeAndCategory(
            string settingId, SettingValueType expectedType, double expectedDefault, double expectedMinimum, double expectedMaximum, string expectedCategory)
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);

            Assert.IsTrue(simulation.Settings.TryGetDefinition(settingId, out SettingDefinition definition), $"Expected '{settingId}' to be declared.");
            Assert.AreEqual(expectedType, definition.ValueType);
            Assert.AreEqual(expectedDefault, definition.Default);
            Assert.AreEqual(expectedMinimum, definition.Minimum);
            Assert.AreEqual(expectedMaximum, definition.Maximum);
            Assert.AreEqual(expectedCategory, definition.Category);
            Assert.AreEqual(expectedDefault, simulation.Settings.Get(settingId), "The live value should start at the default.");
        }

        [Test]
        public void WorldDimensions_DefaultTo10240MeterSquareAtOneMeterCellsAnd256MeterChunks()
        {
            MapModule map = BuildFromRealContent();

            Assert.AreEqual(10240.0, map.WorldWidthMeters);
            Assert.AreEqual(10240.0, map.WorldDepthMeters);
            Assert.AreEqual(1.0, map.CellSizeMeters);
            Assert.AreEqual(256.0, map.ChunkSizeMeters);
        }

        [Test]
        public void WorldToCell_AtTheWorldCenter_IsCorrect()
        {
            MapModule map = BuildFromRealContent();
            Assert.AreEqual(new CellCoord(5120, 5120), map.WorldToCell(5120, 5120));
        }

        [Test]
        public void WorldToCell_AtTheOrigin_IsTheFirstCell()
        {
            MapModule map = BuildFromRealContent();
            Assert.AreEqual(new CellCoord(0, 0), map.WorldToCell(0, 0));
        }

        [Test]
        public void WorldToCell_JustInsideTheFarEdge_IsTheLastCell()
        {
            MapModule map = BuildFromRealContent();
            Assert.AreEqual(new CellCoord(10239, 10239), map.WorldToCell(10239.5, 10239.5));
        }

        [Test]
        public void WorldToChunk_AtEachCorner_IsCorrect()
        {
            MapModule map = BuildFromRealContent();

            Assert.AreEqual(new ChunkCoord(0, 0), map.WorldToChunk(0, 0));
            Assert.AreEqual(new ChunkCoord(map.ChunkCountX - 1, 0), map.WorldToChunk(10239, 0));
            Assert.AreEqual(new ChunkCoord(0, map.ChunkCountZ - 1), map.WorldToChunk(0, 10239));
            Assert.AreEqual(new ChunkCoord(map.ChunkCountX - 1, map.ChunkCountZ - 1), map.WorldToChunk(10239, 10239));
        }

        [Test]
        public void CellToChunk_AgreesWithWorldToChunk()
        {
            MapModule map = BuildFromRealContent();
            CellCoord cell = map.WorldToCell(300, 600);
            Assert.AreEqual(map.WorldToChunk(300, 600), map.CellToChunk(cell));
        }

        [Test]
        public void ClampToWorld_KeepsPositionsInsideTheWorldsEdges()
        {
            MapModule map = BuildFromRealContent();

            (double x, double z) = map.ClampToWorld(-500, 20000);
            Assert.AreEqual(0.0, x);
            Assert.AreEqual(10240.0, z);
        }

        [Test]
        public void ChunkOrigin_ForTheFirstAndLastChunk_IsCorrect()
        {
            MapModule map = BuildFromRealContent();

            (double originX, double originZ) = map.ChunkOrigin(new ChunkCoord(0, 0));
            Assert.AreEqual(0.0, originX);
            Assert.AreEqual(0.0, originZ);

            var lastChunk = new ChunkCoord(map.ChunkCountX - 1, map.ChunkCountZ - 1);
            (double lastOriginX, double lastOriginZ) = map.ChunkOrigin(lastChunk);
            Assert.AreEqual((map.ChunkCountX - 1) * map.ChunkSizeMeters, lastOriginX);
            Assert.AreEqual((map.ChunkCountZ - 1) * map.ChunkSizeMeters, lastOriginZ);
        }

        [Test]
        public void GetOrCreateChunk_ReturnsTheSameInstanceEveryTime_AndIsLazy()
        {
            MapModule map = BuildFromRealContent();
            var coord = new ChunkCoord(3, 4);

            Assert.IsFalse(map.TryGetChunk(coord, out _), "A chunk should not exist before it's first asked for.");

            ChunkData first = map.GetOrCreateChunk(coord);
            ChunkData second = map.GetOrCreateChunk(coord);

            Assert.AreSame(first, second);
            Assert.IsTrue(map.TryGetChunk(coord, out ChunkData found));
            Assert.AreSame(first, found);
        }

        [Test]
        public void GetOrCreateChunk_SameSeedAndChunk_AlwaysGivesTheSamePlacements()
        {
            MapModule first = BuildFromRealContent();
            MapModule second = BuildFromRealContent();
            var coord = new ChunkCoord(12, 7);

            IReadOnlyList<ChunkPlacement> placementsA = first.GetOrCreateChunk(coord).Placements;
            IReadOnlyList<ChunkPlacement> placementsB = second.GetOrCreateChunk(coord).Placements;

            Assert.AreEqual(placementsA.Count, placementsB.Count);
            Assert.Greater(placementsA.Count, 0);

            for (int i = 0; i < placementsA.Count; i++)
            {
                Assert.AreEqual(placementsA[i].Kind, placementsB[i].Kind);
                Assert.AreEqual(placementsA[i].Cell, placementsB[i].Cell);
            }
        }

        [Test]
        public void GetOrCreateChunk_PlacesTheRequestedNumberOfTreesStonesAndGrubMounds()
        {
            MapModule map = BuildFromRealContent();
            ChunkData chunk = map.GetOrCreateChunk(new ChunkCoord(1, 1));

            Assert.AreEqual(20, chunk.Placements.Count(p => p.Kind == PlacementKind.Tree));
            Assert.AreEqual(3, chunk.Placements.Count(p => p.Kind == PlacementKind.Stone));
            Assert.AreEqual(2, chunk.Placements.Count(p => p.Kind == PlacementKind.GrubMound));
        }

        [Test]
        public void IsCellFree_ReturnsFalseOnlyForCellsAPlacementCovers()
        {
            MapModule map = BuildFromRealContent();
            ChunkData chunk = map.GetOrCreateChunk(new ChunkCoord(2, 2));

            var occupied = new HashSet<CellCoord>();
            foreach (ChunkPlacement placement in chunk.Placements)
            {
                int footprint = placement.Kind == PlacementKind.Stone ? 2 : 1;
                for (int dx = 0; dx < footprint; dx++)
                {
                    for (int dz = 0; dz < footprint; dz++)
                    {
                        occupied.Add(new CellCoord(placement.Cell.X + dx, placement.Cell.Z + dz));
                    }
                }
            }

            foreach (CellCoord cell in occupied)
            {
                Assert.IsFalse(map.IsCellFree(cell), $"Expected {cell} to be occupied.");
            }

            // The chunk's own origin corner is vanishingly unlikely to be covered by any of the handful of placements above.
            var chunkOriginCell = new CellCoord(2 * 256, 2 * 256);
            if (!occupied.Contains(chunkOriginCell))
            {
                Assert.IsTrue(map.IsCellFree(chunkOriginCell));
            }
        }

        [Test]
        public void SaveAndLoad_AnAlreadyCreatedChunk_KeepsTheSamePlacements()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);
            var map = (MapModule)modules.First(m => m is MapModule);

            var coord = new ChunkCoord(5, 9);
            List<ChunkPlacement> before = map.GetOrCreateChunk(coord).Placements.ToList();
            Assert.Greater(before.Count, 0);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules);
            var loadedMap = (MapModule)loadedModules.First(m => m is MapModule);

            List<ChunkPlacement> after = loadedMap.GetOrCreateChunk(coord).Placements.ToList();

            Assert.AreEqual(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].Kind, after[i].Kind);
                Assert.AreEqual(before[i].Cell, after[i].Cell);
            }
        }

        [Test]
        public void GetOrCreateChunk_PlacementsNeverOverlap()
        {
            MapModule map = BuildFromRealContent();

            // Several chunks, to exercise more than one random draw.
            foreach (ChunkCoord coord in new[] { new ChunkCoord(0, 0), new ChunkCoord(5, 9), new ChunkCoord(20, 3) })
            {
                ChunkData chunk = map.GetOrCreateChunk(coord);
                var occupied = new HashSet<CellCoord>();

                foreach (ChunkPlacement placement in chunk.Placements)
                {
                    int footprint = placement.Kind == PlacementKind.Stone ? 2 : 1;
                    for (int dx = 0; dx < footprint; dx++)
                    {
                        for (int dz = 0; dz < footprint; dz++)
                        {
                            var cell = new CellCoord(placement.Cell.X + dx, placement.Cell.Z + dz);
                            Assert.IsTrue(occupied.Add(cell), $"Cell {cell} in chunk {coord} is covered by more than one placement.");
                        }
                    }
                }
            }
        }
    }
}
