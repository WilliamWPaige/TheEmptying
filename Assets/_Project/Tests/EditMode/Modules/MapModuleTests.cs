using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>
    /// R6: the shipped map settings (map_settings.json) load with the
    /// right defaults, and MapModule's world/cell/chunk conversions are
    /// correct at the center, the edges and the corners.
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

        [TestCase(MapModule.WorldWidthSettingId, SettingValueType.Number, 15000.0, 256.0, 100000.0, "Map")]
        [TestCase(MapModule.WorldDepthSettingId, SettingValueType.Number, 15000.0, 256.0, 100000.0, "Map")]
        [TestCase(MapModule.CellSizeSettingId, SettingValueType.Number, 1.0, 0.1, 100.0, "Map")]
        [TestCase(MapModule.ChunkSizeSettingId, SettingValueType.Number, 256.0, 8.0, 4096.0, "Map")]
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
        public void WorldDimensions_DefaultTo15KmSquareAtOneMeterCellsAnd256MeterChunks()
        {
            MapModule map = BuildFromRealContent();

            Assert.AreEqual(15000.0, map.WorldWidthMeters);
            Assert.AreEqual(15000.0, map.WorldDepthMeters);
            Assert.AreEqual(1.0, map.CellSizeMeters);
            Assert.AreEqual(256.0, map.ChunkSizeMeters);
        }

        [Test]
        public void WorldToCell_AtTheWorldCenter_IsCorrect()
        {
            MapModule map = BuildFromRealContent();
            Assert.AreEqual(new CellCoord(7500, 7500), map.WorldToCell(7500, 7500));
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
            Assert.AreEqual(new CellCoord(14999, 14999), map.WorldToCell(14999.5, 14999.5));
        }

        [Test]
        public void WorldToChunk_AtEachCorner_IsCorrect()
        {
            MapModule map = BuildFromRealContent();

            Assert.AreEqual(new ChunkCoord(0, 0), map.WorldToChunk(0, 0));
            Assert.AreEqual(new ChunkCoord(map.ChunkCountX - 1, 0), map.WorldToChunk(14999, 0));
            Assert.AreEqual(new ChunkCoord(0, map.ChunkCountZ - 1), map.WorldToChunk(0, 14999));
            Assert.AreEqual(new ChunkCoord(map.ChunkCountX - 1, map.ChunkCountZ - 1), map.WorldToChunk(14999, 14999));
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
            Assert.AreEqual(15000.0, z);
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
    }
}
