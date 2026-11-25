using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Ponds;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation;
using TheEmptying.Presentation.Zones;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>R18b: zones as painted cells - paint validity, slot/capacity math from cells, default naming against real zones, the old-rectangle-save migration, and the save/load round trip.</summary>
    public class ZonesModuleTests
    {
        private static (Simulation Simulation, MapModule Map, BuildingsModule Buildings, PondsModule Ponds, ZonesModule Zones) BuildNewGame(TempCoreContentCopy core, ulong seed = 1)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (
                simulation,
                (MapModule)modules.First(m => m is MapModule),
                (BuildingsModule)modules.First(m => m is BuildingsModule),
                (PondsModule)modules.First(m => m is PondsModule),
                (ZonesModule)modules.First(m => m is ZonesModule));
        }

        private static HashSet<CellCoord> RectCells(MapModule map, double centerX, double centerZ, int width, int depth)
        {
            CellCoord center = map.WorldToCell(centerX, centerZ);
            var cells = new HashSet<CellCoord>();
            for (int dx = 0; dx < width; dx++)
            {
                for (int dz = 0; dz < depth; dz++)
                {
                    cells.Add(new CellCoord(center.X + dx, center.Z + dz));
                }
            }

            return cells;
        }

        #region Slot and capacity math (now from cell count)

        [TestCase(16, 4)]
        [TestCase(10, 2)]
        [TestCase(3, 0)]
        [TestCase(0, 0)]
        public void SlotCount_IsCellCountDividedByCellsPerSlotRoundedDown(int cellCount, int expectedSlots)
        {
            using var core = new TempCoreContentCopy();
            (_, _, _, _, ZonesModule zones) = BuildNewGame(core);

            Assert.AreEqual(expectedSlots, zones.SlotCount(cellCount));
        }

        [TestCase(1, 100)]
        [TestCase(4, 25)]
        [TestCase(3, 33)]
        [TestCase(10, 10)]
        public void CapacityPerSlot_IsOneHundredDividedBySize(int size, int expectedCapacity)
        {
            Assert.AreEqual(expectedCapacity, ZonesModule.CapacityPerSlot(size));
        }

        [Test]
        public void SlotsNeededFor_AnItemCanSpanSeveralSlots()
        {
            Assert.AreEqual(3, ZonesModule.SlotsNeededFor(250, itemSize: 1));
            Assert.AreEqual(1, ZonesModule.SlotsNeededFor(100, itemSize: 1));
            Assert.AreEqual(0, ZonesModule.SlotsNeededFor(0, itemSize: 1));
        }

        #endregion

        #region Zones are cells, which may be several separate pieces

        [Test]
        public void CreateZone_StoresExactlyTheGivenCells_IncludingSeveralSeparatePieces()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            CellCoord pieceA = map.WorldToCell(map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5);
            CellCoord pieceB = new CellCoord(pieceA.X + 50, pieceA.Z + 50); // far enough to never touch pieceA

            EntityId id = zones.CreateZone(ZonesModule.LivingAreaTypeId, "Living Area Zone", "#8BC34A", new[] { pieceA, pieceB });

            Assert.IsTrue(zones.TryGetZone(id, out ZoneState zone));
            Assert.AreEqual(2, zone.Cells.Count);
            Assert.IsTrue(zone.Cells.Contains(pieceA));
            Assert.IsTrue(zone.Cells.Contains(pieceB));
        }

        [Test]
        public void CreateZone_StoresNameAndColor()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            EntityId id = zones.CreateZone(ZonesModule.StockpileTypeId, "My Stockpile", "#112233", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4));

            Assert.IsTrue(zones.TryGetZone(id, out ZoneState zone));
            Assert.AreEqual("My Stockpile", zone.Name);
            Assert.AreEqual("#112233", zone.ColorHex);
        }

        #endregion

        #region Paint validity

        [Test]
        public void CanPaintCell_RejectsACellAlreadyInAnotherZone()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> cells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);
            zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells);

            CellCoord claimed = cells.First();
            Assert.IsFalse(zones.CanPaintCell(claimed));

            CellCoord free = new CellCoord(claimed.X + 100, claimed.Z + 100);
            Assert.IsTrue(zones.CanPaintCell(free));
        }

        [Test]
        public void CanPaintCell_RejectsACellInsideACrownBuilding()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, BuildingsModule buildings, _, ZonesModule zones) = BuildNewGame(core);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            buildings.PlaceBuilding(BuildingsModule.KeepContentId, centerX, centerZ, 12, 12);

            CellCoord insideKeep = map.WorldToCell(centerX, centerZ);
            Assert.IsFalse(zones.CanPaintCell(insideKeep));
        }

        [Test]
        public void CanPaintCell_RejectsPondWater()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, PondsModule ponds, ZonesModule zones) = BuildNewGame(core);

            PondState pond = ponds.Ponds.First().Value;
            CellCoord pondCenterCell = map.WorldToCell(pond.CenterX, pond.CenterZ);

            Assert.IsFalse(zones.CanPaintCell(pondCenterCell));
        }

        [Test]
        public void CanPaintCell_AllowsAnEmptyCell()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            // Far from the guaranteed starting pond and anything else.
            CellCoord farCell = map.WorldToCell(map.WorldWidthMeters * 0.1, map.WorldDepthMeters * 0.1);
            Assert.IsTrue(zones.CanPaintCell(farCell));
        }

        #endregion

        #region Default naming against real zones

        [Test]
        public void ZoneNames_ThenNextDefaultName_CountsUpAgainstRealZones()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4));

            string nextName = ZoneNaming.NextDefaultName("Stockpile Zone", zones.ZoneNames);
            Assert.AreEqual("Stockpile Zone 2", nextName);
        }

        #endregion

        #region Stockpile add logic (capacity now from cell count)

        [Test]
        public void TryAddItemToStockpile_ANewStockpileStartsEmpty()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4));

            (int amount, int limit, bool blocked) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(0, amount);
            Assert.IsFalse(blocked);
            Assert.Greater(limit, 0);
        }

        [Test]
        public void TryAddItemToStockpile_AddsUpToSlotCapacity_AndReportsWhatDidNotFit()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            // 16 cells / 4 per slot = 4 slots; item.grub is size 1, so 100 per slot = 400 max.
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4));

            (int added, int didNotFit, string reason) = zones.TryAddItemToStockpile(zoneId, "item.grub", 450);

            Assert.AreEqual(400, added);
            Assert.AreEqual(50, didNotFit);
            Assert.IsNotEmpty(reason);

            (int amount, _, _) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(400, amount);
        }

        [Test]
        public void TryAddItemToStockpile_RespectsTheKeepOnHandLimit()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4));

            zones.SetKeepOnHandLimit(zoneId, "item.grub", 10);

            (int added, int didNotFit, string reason) = zones.TryAddItemToStockpile(zoneId, "item.grub", 50);

            Assert.AreEqual(10, added);
            Assert.AreEqual(40, didNotFit);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void TryAddItemToStockpile_RespectsABlock()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4));

            zones.SetBlocked(zoneId, "item.grub", true);

            (int added, int didNotFit, string reason) = zones.TryAddItemToStockpile(zoneId, "item.grub", 10);

            Assert.AreEqual(0, added);
            Assert.AreEqual(10, didNotFit);
            Assert.IsNotEmpty(reason);

            zones.SetBlocked(zoneId, "item.grub", false);
            (int addedAfterUnblock, int didNotFitAfterUnblock, _) = zones.TryAddItemToStockpile(zoneId, "item.grub", 10);
            Assert.AreEqual(10, addedAfterUnblock);
            Assert.AreEqual(0, didNotFitAfterUnblock);
        }

        #endregion

        #region Save and load

        [Test]
        public void SaveAndLoad_ZonesAndStockpileContentsLimitsAndBlocks_SurviveTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            EntityId stockpileId = zones.CreateZone(ZonesModule.StockpileTypeId, "Grub Stockpile", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4));
            zones.TryAddItemToStockpile(stockpileId, "item.grub", 120);
            zones.SetKeepOnHandLimit(stockpileId, "item.grub", 300);
            zones.SetBlocked(stockpileId, "item.log", true);

            HashSet<CellCoord> livingAreaCells = RectCells(map, map.WorldWidthMeters * 0.5 + 50, map.WorldDepthMeters * 0.5, 10, 8);
            EntityId livingAreaId = zones.CreateZone(ZonesModule.LivingAreaTypeId, "My Living Area", "#8BC34A", livingAreaCells);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var loadedZones = (ZonesModule)loadedModules.First(m => m is ZonesModule);

            Assert.AreEqual(2, loadedZones.Zones.Count());

            Assert.IsTrue(loadedZones.TryGetZone(stockpileId, out ZoneState stockpile));
            Assert.AreEqual(ZonesModule.StockpileTypeId, stockpile.ZoneTypeId);
            Assert.AreEqual("Grub Stockpile", stockpile.Name);
            Assert.AreEqual("#3DA5D9", stockpile.ColorHex);
            Assert.AreEqual(24, stockpile.Cells.Count);

            (int amount, int limit, bool grubBlocked) = loadedZones.GetStockpileItemInfo(stockpileId, "item.grub");
            Assert.AreEqual(120, amount);
            Assert.AreEqual(300, limit);
            Assert.IsFalse(grubBlocked);

            (_, _, bool logBlocked) = loadedZones.GetStockpileItemInfo(stockpileId, "item.log");
            Assert.IsTrue(logBlocked);

            Assert.IsTrue(loadedZones.TryGetZone(livingAreaId, out ZoneState livingArea));
            Assert.AreEqual(ZonesModule.LivingAreaTypeId, livingArea.ZoneTypeId);
            Assert.AreEqual("My Living Area", livingArea.Name);
            Assert.AreEqual(livingAreaCells.Count, livingArea.Cells.Count);
            CollectionAssert.AreEquivalent(livingAreaCells, livingArea.Cells);
        }

        [Test]
        public void Read_OldRectangleFormat_ConvertsToCellsWithDefaultNameAndTypeTint()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            EntityId id = simulation.Entities.Create(EntityKind.Zone);

            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            writer.WriteInt(1);
            writer.WriteEntityId(id);
            writer.WriteString(ZonesModule.StockpileTypeId);
            writer.WriteDouble(map.WorldWidthMeters * 0.5);
            writer.WriteDouble(map.WorldDepthMeters * 0.5);
            writer.WriteDouble(6);
            writer.WriteDouble(4);
            writer.WriteDouble(0);
            writer.WriteInt(0);

            stream.Position = 0;
            var reader = new SaveReader(stream);
            zones.Read(reader, savedVersion: 1);

            Assert.AreEqual(1, zones.Zones.Count());
            Assert.IsTrue(zones.TryGetZone(id, out ZoneState zone));
            Assert.AreEqual(ZonesModule.StockpileTypeId, zone.ZoneTypeId);
            Assert.Greater(zone.Cells.Count, 0);
            Assert.AreEqual("Stockpile Zone", zone.Name);
            Assert.AreEqual("#3DA5D9", zone.ColorHex);
        }

        #endregion
    }
}
