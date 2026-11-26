using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
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

        #region Capacity math

        [TestCase(1, 100)]
        [TestCase(4, 25)]
        [TestCase(3, 33)]
        [TestCase(10, 10)]
        public void CapacityPerSpot_IsOneHundredDividedBySize(int size, int expectedCapacity)
        {
            using var core = new TempCoreContentCopy();
            (_, _, _, _, ZonesModule zones) = BuildNewGame(core);

            Assert.AreEqual(expectedCapacity, zones.CapacityPerSpot(size));
        }

        [Test]
        public void CapacityPerSpot_ForRock_IsOneHundred()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, _, _, ZonesModule zones) = BuildNewGame(core);

            Assert.IsTrue(simulation.Content.TryGet(BuiltInContentTypes.ItemTypeName, "item.rock", out ContentEntry entry));
            var rock = (ItemContent)entry.Definition;

            Assert.AreEqual(1, rock.Size);
            Assert.AreEqual(100, zones.CapacityPerSpot(rock.Size));
        }

        #endregion

        #region Spots and layouts

        [Test]
        public void UniformLayout_LaysOutARegularGridOfSpots_AllInsideThePaint()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> cells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(ZonesModule.LayoutUniformId, zone.LayoutId);
            Assert.AreEqual(4, zone.Spots.Count); // 16 cells / (2x2 m spot = 4 cells) = 4 spots

            foreach (StockSpot spot in zone.Spots)
            {
                CellCoord spotCell = map.WorldToCell(spot.CenterX, spot.CenterZ);
                Assert.IsTrue(cells.Contains(spotCell), "Every spot should land inside the painted cells.");
            }
        }

        [Test]
        public void NaturalLayout_SpotsAreInsideThePaintAndNeverOverlap_AndAreTheSameForTheSameSeed()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map1, _, _, ZonesModule zones1) = BuildNewGame(core, seed: 42);
            (_, MapModule map2, _, _, ZonesModule zones2) = BuildNewGame(core, seed: 42);

            HashSet<CellCoord> cells1 = RectCells(map1, map1.WorldWidthMeters * 0.5, map1.WorldDepthMeters * 0.5, 20, 20);
            HashSet<CellCoord> cells2 = RectCells(map2, map2.WorldWidthMeters * 0.5, map2.WorldDepthMeters * 0.5, 20, 20);

            EntityId zoneId1 = zones1.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells1, ZonesModule.LayoutNaturalId);
            EntityId zoneId2 = zones2.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells2, ZonesModule.LayoutNaturalId);

            Assert.IsTrue(zones1.TryGetZone(zoneId1, out ZoneState zone1));
            Assert.IsTrue(zones2.TryGetZone(zoneId2, out ZoneState zone2));

            Assert.Greater(zone1.Spots.Count, 0, "A 20x20 cell area should fit at least one Natural patch.");
            Assert.AreEqual(zone1.Spots.Count, zone2.Spots.Count, "The same seed should give the same spot count.");

            for (int i = 0; i < zone1.Spots.Count; i++)
            {
                Assert.AreEqual(zone1.Spots[i].CenterX, zone2.Spots[i].CenterX, 0.0001);
                Assert.AreEqual(zone1.Spots[i].CenterZ, zone2.Spots[i].CenterZ, 0.0001);
                Assert.AreEqual(zone1.Spots[i].RotationDegrees, zone2.Spots[i].RotationDegrees, 0.0001);

                CellCoord spotCell = map1.WorldToCell(zone1.Spots[i].CenterX, zone1.Spots[i].CenterZ);
                Assert.IsTrue(cells1.Contains(spotCell), "Every spot should land inside the painted cells.");

                for (int j = i + 1; j < zone1.Spots.Count; j++)
                {
                    double dx = zone1.Spots[i].CenterX - zone1.Spots[j].CenterX;
                    double dz = zone1.Spots[i].CenterZ - zone1.Spots[j].CenterZ;
                    double distance = System.Math.Sqrt(dx * dx + dz * dz);
                    Assert.GreaterOrEqual(distance, 1.99, "No two spots should overlap.");
                }
            }
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

        #region Stockpile add and take logic

        [Test]
        public void TryAddItemToStockpile_ANewStockpileStartsEmpty()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4), ZonesModule.LayoutUniformId);

            (int amount, int limit, bool blocked) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(0, amount);
            Assert.IsFalse(blocked);
            Assert.Greater(limit, 0);
        }

        [Test]
        public void TryAddItemToStockpile_AddsUpToSpotCapacity_AndReportsWhatDidNotFit()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            // 16 cells / (2x2 m spot = 4 cells) = 4 spots; item.grub is size 1, so 100 per spot = 400 max.
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4), ZonesModule.LayoutUniformId);

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
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4), ZonesModule.LayoutUniformId);

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
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4), ZonesModule.LayoutUniformId);

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

        [Test]
        public void TryAddItemToStockpile_FillsAnExistingStackFirst_BeforeStartingAnotherSpot()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4), ZonesModule.LayoutUniformId);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 30);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zoneAfterFirst));
            Assert.AreEqual(1, zoneAfterFirst.Spots.Count(s => s.AmountStored > 0));

            zones.TryAddItemToStockpile(zoneId, "item.grub", 20);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zoneAfterSecond));
            Assert.AreEqual(1, zoneAfterSecond.Spots.Count(s => s.AmountStored > 0), "The second delivery should top up the same stack, not start a new one.");
            Assert.AreEqual(50, zoneAfterSecond.Spots.Single(s => s.AmountStored > 0).AmountStored);
        }

        [Test]
        public void TryAddItemToStockpile_ADeliveryLargerThanOneSpot_SpillsIntoTheNextSpot()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4), ZonesModule.LayoutUniformId);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 100); // fills exactly one spot
            (int added, int didNotFit, _) = zones.TryAddItemToStockpile(zoneId, "item.grub", 150);

            Assert.AreEqual(150, added);
            Assert.AreEqual(0, didNotFit);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(2, zone.Spots.Count(s => s.AmountStored == 100));
            Assert.AreEqual(1, zone.Spots.Count(s => s.AmountStored == 50));

            (int amount, _, _) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(250, amount);
        }

        [Test]
        public void RemoveItemFromStockpile_TakesFromTheLeastFullStackFirst_AndSpillsToTheNextLeastFullStack()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4), ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            StockSpot fullerSpot = zone.Spots[0];
            StockSpot leastFullSpot = zone.Spots[1];
            fullerSpot.ItemContentId = "item.grub";
            fullerSpot.AmountStored = 40;
            leastFullSpot.ItemContentId = "item.grub";
            leastFullSpot.AmountStored = 10;

            int removed = zones.RemoveItemFromStockpile(zoneId, "item.grub", 15);

            Assert.AreEqual(15, removed);
            Assert.AreEqual(0, leastFullSpot.AmountStored);
            Assert.IsNull(leastFullSpot.ItemContentId, "An emptied spot should become free for any item's next stack.");
            Assert.AreEqual(35, fullerSpot.AmountStored);
        }

        #endregion

        #region Save and load

        [Test]
        public void SaveAndLoad_ZonesAndStockpileContentsLimitsAndBlocks_SurviveTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            EntityId stockpileId = zones.CreateZone(ZonesModule.StockpileTypeId, "Grub Stockpile", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4), ZonesModule.LayoutUniformId);
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
            Assert.AreEqual(ZonesModule.LayoutUniformId, stockpile.LayoutId);
            Assert.AreEqual(6, stockpile.Spots.Count); // 24 cells / (2x2 m spot = 4 cells) = 6 spots

            (int amount, int limit, bool grubBlocked) = loadedZones.GetStockpileItemInfo(stockpileId, "item.grub");
            Assert.AreEqual(120, amount);
            Assert.AreEqual(300, limit);
            Assert.IsFalse(grubBlocked);
            Assert.AreEqual(120, stockpile.Spots.Sum(s => s.ItemContentId == "item.grub" ? s.AmountStored : 0));

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

        [Test]
        public void Read_OldStockpileAmountFormat_LaysStoredAmountsIntoSpots()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            EntityId id = simulation.Entities.Create(EntityKind.Zone);
            HashSet<CellCoord> cells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 20, 20);

            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            writer.WriteInt(1); // zone count
            writer.WriteEntityId(id);
            writer.WriteString(ZonesModule.StockpileTypeId);
            writer.WriteString("Grub Stockpile");
            writer.WriteString("#3DA5D9");
            writer.WriteDouble(0);
            writer.WriteDouble(0);
            writer.WriteInt(cells.Count);
            foreach (CellCoord cell in cells.OrderBy(c => c.X).ThenBy(c => c.Z))
            {
                writer.WriteInt(cell.X);
                writer.WriteInt(cell.Z);
            }

            // Old format (SectionVersion < 3): one entry per known item - id, amount stored, keep-on-hand override, blocked.
            writer.WriteInt(1);
            writer.WriteString("item.grub");
            writer.WriteInt(50);
            writer.WriteBool(false);
            writer.WriteInt(0);
            writer.WriteBool(false);

            stream.Position = 0;
            var reader = new SaveReader(stream);
            zones.Read(reader, savedVersion: 2);

            Assert.IsTrue(zones.TryGetZone(id, out ZoneState zone));
            Assert.AreEqual(ZonesModule.LayoutNaturalId, zone.LayoutId);
            Assert.Greater(zone.Spots.Count, 0, "The migration should lay out a fresh Natural layout for the old zone's cells.");

            (int amount, _, _) = zones.GetStockpileItemInfo(id, "item.grub");
            Assert.AreEqual(50, amount);
        }

        #endregion
    }
}
