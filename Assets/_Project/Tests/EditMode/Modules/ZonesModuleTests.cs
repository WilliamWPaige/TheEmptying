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

        [TestCase(PileShape.Quarter, 1, 25)]
        [TestCase(PileShape.Quarter, 4, 6)]
        [TestCase(PileShape.Half, 4, 12)]
        [TestCase(PileShape.Full, 6, 16)]
        [TestCase(PileShape.Full, 1, 100)]
        public void CapacityPerPile_IsCapacityUnitsPerQuarterTimesQuartersCovered_DividedBySize_RoundedDown_MinimumOne(PileShape shape, int size, int expectedCapacity)
        {
            using var core = new TempCoreContentCopy();
            (_, _, _, _, ZonesModule zones) = BuildNewGame(core);

            Assert.AreEqual(expectedCapacity, zones.CapacityPerPile(shape, size));
        }

        [Test]
        public void CapacityPerPile_MatchesTheRealItems_StickLogAndStone()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, _, _, ZonesModule zones) = BuildNewGame(core);

            Assert.IsTrue(simulation.Content.TryGet(BuiltInContentTypes.ItemTypeName, "item.stick", out ContentEntry stickEntry));
            Assert.IsTrue(simulation.Content.TryGet(BuiltInContentTypes.ItemTypeName, "item.log", out ContentEntry logEntry));
            Assert.IsTrue(simulation.Content.TryGet(BuiltInContentTypes.ItemTypeName, "item.stone", out ContentEntry stoneEntry));

            var stick = (ItemContent)stickEntry.Definition;
            var log = (ItemContent)logEntry.Definition;
            var stone = (ItemContent)stoneEntry.Definition;

            Assert.AreEqual(PileShape.Quarter, stick.PileShape);
            Assert.AreEqual(PileShape.Half, log.PileShape);
            Assert.AreEqual(PileShape.Full, stone.PileShape);

            Assert.AreEqual(25, zones.CapacityPerPile(stick.PileShape, stick.Size));
            Assert.AreEqual(12, zones.CapacityPerPile(log.PileShape, log.Size));
            Assert.AreEqual(16, zones.CapacityPerPile(stone.PileShape, stone.Size));
        }

        #endregion

        #region Pile shapes and mixing

        [Test]
        public void TryAddItemToStockpile_MixesDifferentItems_InTheSameSpotsSeparateQuarters()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 2, 2), ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(1, zone.Spots.Count, "A single 2x2 m Uniform spot.");

            zones.TryAddItemToStockpile(zoneId, "item.grub", 25); // Quarter shape - one quarter
            zones.TryAddItemToStockpile(zoneId, "item.stick", 25); // Quarter shape - another quarter

            StockSpot spot = zone.Spots[0];
            Assert.AreEqual(2, spot.Piles.Count, "Two different items should share the one spot, each in its own quarter.");

            StockPile grubPile = spot.Piles.Single(p => p.ItemContentId == "item.grub");
            StockPile stickPile = spot.Piles.Single(p => p.ItemContentId == "item.stick");
            CollectionAssert.IsEmpty(grubPile.QuarterIndices.Intersect(stickPile.QuarterIndices), "The two piles must not share a quarter.");
        }

        [Test]
        public void TryAddItemToStockpile_HalfShape_TakesTwoNeighboringQuarters_AndBlocksAFullShapePileFromJoiningTheSpot()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 2, 2), ZonesModule.LayoutUniformId);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));

            zones.TryAddItemToStockpile(zoneId, "item.log", 1); // item.log is Half shape

            StockSpot spot = zone.Spots[0];
            StockPile logPile = spot.Piles.Single();
            Assert.AreEqual(2, logPile.QuarterIndices.Count);

            int[] quarters = logPile.QuarterIndices.OrderBy(q => q).ToArray();
            bool isEdgeAdjacentPair = (quarters[0] == 0 && quarters[1] == 1)
                || (quarters[0] == 2 && quarters[1] == 3)
                || (quarters[0] == 0 && quarters[1] == 2)
                || (quarters[0] == 1 && quarters[1] == 3);
            Assert.IsTrue(isEdgeAdjacentPair, "A Half pile must take two edge-adjacent quarters, never a diagonal pair.");

            (int added, int didNotFit, _) = zones.TryAddItemToStockpile(zoneId, "item.stone", 1); // item.stone is Full shape - needs all 4 quarters
            Assert.AreEqual(0, added);
            Assert.AreEqual(1, didNotFit, "A Full pile needs all four quarters free, so it can't join a spot that already holds a Half pile.");
            Assert.AreEqual(1, spot.Piles.Count);
        }

        [Test]
        public void TryAddItemToStockpile_FullShape_OnlyFitsInACompletelyEmptySpot()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 2, 2), ZonesModule.LayoutUniformId);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));

            zones.TryAddItemToStockpile(zoneId, "item.grub", 1); // takes one quarter

            (int added, int didNotFit, _) = zones.TryAddItemToStockpile(zoneId, "item.stone", 1); // Full shape needs all 4 quarters
            Assert.AreEqual(0, added);
            Assert.AreEqual(1, didNotFit, "A Full pile can't start where even one quarter is already taken.");
        }

        #endregion

        #region Delivery order

        [Test]
        public void TryAddItemToStockpile_NewPile_PrefersASpotThatAlreadyHoldsTheItem_OverAnUntouchedSpot()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 2), ZonesModule.LayoutUniformId);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(3, zone.Spots.Count);

            // Spot 2 (the far spot) already holds a full grub pile in one quarter.
            var seedPile = new StockPile { ItemContentId = "item.grub", AmountStored = 25 };
            seedPile.QuarterIndices.Add(0);
            zone.Spots[2].Piles.Add(seedPile);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 25); // the existing pile is full, so this must start a new one

            Assert.AreEqual(2, zone.Spots[2].Piles.Count, "The new pile should land in the spot that already holds this item, even though it's farther than the untouched spots.");
            Assert.AreEqual(0, zone.Spots[0].Piles.Count);
            Assert.AreEqual(0, zone.Spots[1].Piles.Count);
        }

        [Test]
        public void TryAddItemToStockpile_NewPile_PrefersASpotPartlyUsedByAnotherItem_OverAnUntouchedEmptySpot()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 2), ZonesModule.LayoutUniformId);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(3, zone.Spots.Count);

            // Spot 1 (the middle spot) already holds sticks in one quarter - "partly used" by another item.
            var stickPile = new StockPile { ItemContentId = "item.stick", AmountStored = 25 };
            stickPile.QuarterIndices.Add(0);
            zone.Spots[1].Piles.Add(stickPile);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 25); // grub has never been stored here - no same-item spot exists

            Assert.AreEqual(1, zone.Spots[1].Piles.Count(p => p.ItemContentId == "item.grub"), "The new grub pile should land in the spot already partly used by sticks, not an untouched empty spot.");
            Assert.AreEqual(0, zone.Spots[0].Piles.Count);
            Assert.AreEqual(0, zone.Spots[2].Piles.Count);
        }

        [Test]
        public void TryAddItemToStockpile_NewPile_ChoosesTheEmptySpotNearestThisItemsOtherPiles()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 2), ZonesModule.LayoutUniformId);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(3, zone.Spots.Count);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 100); // fills spot 0 completely (4 quarters x 25)
            Assert.IsTrue(zone.Spots[0].Piles.All(p => p.AmountStored == 25) && zone.Spots[0].Piles.Count == 4);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 1); // spot 0 has no free quarters left - must start a new pile elsewhere

            Assert.Greater(zone.Spots[1].Piles.Count, 0, "The next delivery should land in the nearest empty spot to grub's existing piles.");
            Assert.AreEqual(0, zone.Spots[2].Piles.Count, "The farther empty spot should be untouched.");
        }

        #endregion

        #region Occupancy with mixed piles

        [Test]
        public void IsZoneOccupied_AndGetOccupiedCells_TreatASpotWithAnyPileAsOccupied_EvenWhenMixed()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 2, 2), ZonesModule.LayoutUniformId);

            Assert.IsFalse(zones.IsZoneOccupied(zoneId));
            Assert.AreEqual(0, zones.GetOccupiedCells(zoneId).Count);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 1);
            zones.TryAddItemToStockpile(zoneId, "item.stick", 1); // a second, different item in the same spot's other quarter

            Assert.IsTrue(zones.IsZoneOccupied(zoneId), "A spot holding two different items' piles is still just one occupied spot.");
            Assert.Greater(zones.GetOccupiedCells(zoneId).Count, 0);

            zones.RemoveItemFromStockpile(zoneId, "item.grub", 1);
            Assert.IsTrue(zones.IsZoneOccupied(zoneId), "The spot is still occupied while the stick pile remains.");

            zones.RemoveItemFromStockpile(zoneId, "item.stick", 1);
            Assert.IsFalse(zones.IsZoneOccupied(zoneId), "Emptying every pile should free the spot.");
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
            Assert.AreEqual(1, zoneAfterFirst.Spots.Count(s => s.TotalStored > 0));

            zones.TryAddItemToStockpile(zoneId, "item.grub", 20);
            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zoneAfterSecond));
            Assert.AreEqual(1, zoneAfterSecond.Spots.Count(s => s.TotalStored > 0), "The second delivery should top up the same spot's piles, not start a new spot.");
            Assert.AreEqual(50, zoneAfterSecond.Spots.Single(s => s.TotalStored > 0).TotalStored);
        }

        [Test]
        public void TryAddItemToStockpile_ADeliveryLargerThanOneSpot_SpillsIntoTheNextSpot()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4), ZonesModule.LayoutUniformId);

            zones.TryAddItemToStockpile(zoneId, "item.grub", 100); // fills exactly one spot's four quarters (25 each)
            (int added, int didNotFit, _) = zones.TryAddItemToStockpile(zoneId, "item.grub", 150);

            Assert.AreEqual(150, added);
            Assert.AreEqual(0, didNotFit);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(2, zone.Spots.Count(s => s.TotalStored == 100));
            Assert.AreEqual(1, zone.Spots.Count(s => s.TotalStored == 50));

            (int amount, _, _) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(250, amount);
        }

        [Test]
        public void RemoveItemFromStockpile_TakesFromTheLeastFullPileFirst_AndSpillsToTheNextLeastFullPile()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4), ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            StockSpot fullerSpot = zone.Spots[0];
            StockSpot leastFullSpot = zone.Spots[1];
            var fullerPile = new StockPile { ItemContentId = "item.grub", AmountStored = 40 };
            fullerPile.QuarterIndices.AddRange(new[] { 0, 1, 2, 3 });
            fullerSpot.Piles.Add(fullerPile);
            var leastFullPile = new StockPile { ItemContentId = "item.grub", AmountStored = 10 };
            leastFullPile.QuarterIndices.AddRange(new[] { 0, 1, 2, 3 });
            leastFullSpot.Piles.Add(leastFullPile);

            int removed = zones.RemoveItemFromStockpile(zoneId, "item.grub", 15);

            Assert.AreEqual(15, removed);
            Assert.IsFalse(leastFullSpot.Piles.Contains(leastFullPile), "An emptied pile should be removed, freeing its quarters.");
            Assert.AreEqual(35, fullerPile.AmountStored);
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
            Assert.AreEqual(120, stockpile.Spots.SelectMany(s => s.Piles).Where(p => p.ItemContentId == "item.grub").Sum(p => p.AmountStored));

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

        [Test]
        public void Read_OldSingleStackPerSpotFormat_SplitsIntoPilesWithinTheSameSpot_AndOverflowsElsewhereIfNeeded()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, _, _, ZonesModule zones) = BuildNewGame(core);

            EntityId id = simulation.Entities.Create(EntityKind.Zone);

            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            writer.WriteInt(1); // zone count
            writer.WriteEntityId(id);
            writer.WriteString(ZonesModule.StockpileTypeId);
            writer.WriteString("Grub Stockpile");
            writer.WriteString("#3DA5D9");
            writer.WriteDouble(0);
            writer.WriteDouble(0);
            writer.WriteInt(0); // no cells needed for this test

            writer.WriteString(ZonesModule.LayoutUniformId); // LayoutId

            // Old (SectionVersion 3, R20/R21) per-spot single-stack format: two spots.
            writer.WriteInt(2);

            // Spot 0 holds 150 grub - more than one spot's own quarters can hold (item.grub is size 1, Quarter shape: 4 quarters x 25 = 100 max per spot).
            writer.WriteDouble(0);
            writer.WriteDouble(0);
            writer.WriteDouble(0);
            writer.WriteString("item.grub");
            writer.WriteInt(150);
            writer.WriteInt(0);

            // Spot 1 is empty.
            writer.WriteDouble(2);
            writer.WriteDouble(0);
            writer.WriteDouble(0);
            writer.WriteString(string.Empty);
            writer.WriteInt(0);
            writer.WriteInt(0);

            writer.WriteInt(0); // no StockpileItems entries

            stream.Position = 0;
            var reader = new SaveReader(stream);
            zones.Read(reader, savedVersion: 3);

            Assert.IsTrue(zones.TryGetZone(id, out ZoneState zone));
            Assert.AreEqual(2, zone.Spots.Count);

            StockSpot spot0 = zone.Spots[0];
            StockSpot spot1 = zone.Spots[1];

            Assert.AreEqual(100, spot0.TotalStored, "The first spot should fill up to its own quarters' capacity before anything overflows elsewhere.");
            Assert.AreEqual(50, spot1.TotalStored, "The rest should overflow into the next spot by the normal delivery rules.");

            (int amount, _, _) = zones.GetStockpileItemInfo(id, "item.grub");
            Assert.AreEqual(150, amount, "Nothing should be lost when it all still fits somewhere in the stockpile.");
        }

        #endregion

        #region Modify and delete

        [Test]
        public void ApplyZoneEdit_KeepsOccupiedCells_EvenIfTheCallerOmitsThem()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> originalCells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", originalCells, ZonesModule.LayoutUniformId);
            zones.TryAddItemToStockpile(zoneId, "item.grub", 10);

            HashSet<CellCoord> occupiedCells = zones.GetOccupiedCells(zoneId);
            Assert.Greater(occupiedCells.Count, 0);

            // A caller (the UI should never actually do this) tries to paint a cell set with every occupied cell left out.
            var attemptedNewCells = new HashSet<CellCoord>(originalCells);
            attemptedNewCells.ExceptWith(occupiedCells);

            zones.ApplyZoneEdit(zoneId, "Stockpile Zone", "#3DA5D9", ZonesModule.StockpileTypeId, attemptedNewCells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            CollectionAssert.IsSubsetOf(occupiedCells, zone.Cells);

            (int amount, _, _) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(10, amount, "The occupied spot's contents must never vanish.");
        }

        [Test]
        public void ApplyZoneEdit_CannotChangeTypeAwayFromAStockpile_WhileItHoldsItems()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> cells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells, ZonesModule.LayoutUniformId);
            zones.TryAddItemToStockpile(zoneId, "item.grub", 10);

            zones.ApplyZoneEdit(zoneId, "Renamed", "#112233", ZonesModule.LivingAreaTypeId, cells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(ZonesModule.StockpileTypeId, zone.ZoneTypeId, "The type change should be blocked while the stockpile holds anything.");
            Assert.AreEqual("Renamed", zone.Name, "The name should still update even though the type change was blocked.");
        }

        [Test]
        public void ApplyZoneEdit_CanChangeType_WhenTheStockpileHoldsNothing()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> cells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells, ZonesModule.LayoutUniformId);

            zones.ApplyZoneEdit(zoneId, "Living Area Zone", "#8BC34A", ZonesModule.LivingAreaTypeId, cells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(ZonesModule.LivingAreaTypeId, zone.ZoneTypeId);
            Assert.AreEqual(0, zone.Spots.Count, "A Living Area has no spots.");
        }

        [Test]
        public void ApplyZoneEdit_KeepsExistingSpotsInPlace_AndAddsNewSpotsOnlyInTheNewArea_WithoutOverlap()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> originalCells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", originalCells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zoneBefore));
            Assert.AreEqual(4, zoneBefore.Spots.Count);
            List<(double X, double Z)> originalPositions = zoneBefore.Spots.Select(s => (s.CenterX, s.CenterZ)).ToList();

            HashSet<CellCoord> expandedCells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 8, 4);
            zones.ApplyZoneEdit(zoneId, "Stockpile Zone", "#3DA5D9", ZonesModule.StockpileTypeId, expandedCells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zoneAfter));
            Assert.AreEqual(8, zoneAfter.Spots.Count);

            foreach ((double x, double z) in originalPositions)
            {
                Assert.IsTrue(
                    zoneAfter.Spots.Any(s => System.Math.Abs(s.CenterX - x) < 0.0001 && System.Math.Abs(s.CenterZ - z) < 0.0001),
                    "An existing spot should stay exactly where it was.");
            }

            for (int i = 0; i < zoneAfter.Spots.Count; i++)
            {
                for (int j = i + 1; j < zoneAfter.Spots.Count; j++)
                {
                    double dx = zoneAfter.Spots[i].CenterX - zoneAfter.Spots[j].CenterX;
                    double dz = zoneAfter.Spots[i].CenterZ - zoneAfter.Spots[j].CenterZ;
                    Assert.GreaterOrEqual(System.Math.Sqrt(dx * dx + dz * dz), 1.99, "No two spots should overlap.");
                }
            }
        }

        [Test]
        public void ApplyZoneEdit_RemovesEmptySpotsThatNoLongerFitInsideTheNewPaint()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> originalCells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", originalCells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zoneBefore));
            Assert.AreEqual(6, zoneBefore.Spots.Count);

            CellCoord anchor = map.WorldToCell(map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5);
            var shrunkCells = new HashSet<CellCoord>(originalCells);

            // Removes exactly one Uniform spot's worth of cells (a 2x2 block, aligned with the grid), leaving no room for a replacement there.
            for (int dx = 4; dx < 6; dx++)
            {
                for (int dz = 0; dz < 2; dz++)
                {
                    shrunkCells.Remove(new CellCoord(anchor.X + dx, anchor.Z + dz));
                }
            }

            zones.ApplyZoneEdit(zoneId, "Stockpile Zone", "#3DA5D9", ZonesModule.StockpileTypeId, shrunkCells, ZonesModule.LayoutUniformId);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zoneAfter));
            Assert.AreEqual(5, zoneAfter.Spots.Count);
        }

        [Test]
        public void DeleteZone_WithNoOccupiedSpots_RemovesTheZoneEntirely()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> cells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells, ZonesModule.LayoutUniformId);

            ZoneDeleteResult result = zones.DeleteZone(zoneId);

            Assert.IsTrue(result.FullyDeleted);
            Assert.AreEqual(0, result.RemainingOccupiedSpots);
            Assert.IsFalse(zones.TryGetZone(zoneId, out _));
        }

        [Test]
        public void DeleteZone_WithOccupiedSpots_KeepsOnlyTheOccupiedSpotsAndCells()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            HashSet<CellCoord> cells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4);
            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", cells, ZonesModule.LayoutUniformId);
            zones.TryAddItemToStockpile(zoneId, "item.grub", 10);

            ZoneDeleteResult result = zones.DeleteZone(zoneId);

            Assert.IsFalse(result.FullyDeleted);
            Assert.AreEqual(1, result.RemainingOccupiedSpots);

            Assert.IsTrue(zones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual(1, zone.Spots.Count);
            Assert.Greater(zone.Spots[0].TotalStored, 0);

            (int amount, _, _) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(10, amount);

            // Deleting again, now that it's empty, removes it entirely.
            zones.RemoveItemFromStockpile(zoneId, "item.grub", 10);
            ZoneDeleteResult secondResult = zones.DeleteZone(zoneId);
            Assert.IsTrue(secondResult.FullyDeleted);
            Assert.IsFalse(zones.TryGetZone(zoneId, out _));
        }

        [Test]
        public void SaveAndLoad_AfterApplyZoneEdit_SurvivesTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, _, _, ZonesModule zones) = BuildNewGame(core);

            EntityId zoneId = zones.CreateZone(ZonesModule.StockpileTypeId, "Stockpile Zone", "#3DA5D9", RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4), ZonesModule.LayoutUniformId);
            zones.TryAddItemToStockpile(zoneId, "item.grub", 10);

            HashSet<CellCoord> expandedCells = RectCells(map, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4);
            zones.ApplyZoneEdit(zoneId, "Modified Name", "#654321", ZonesModule.StockpileTypeId, expandedCells, ZonesModule.LayoutUniformId);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var loadedZones = (ZonesModule)loadedModules.First(m => m is ZonesModule);

            Assert.IsTrue(loadedZones.TryGetZone(zoneId, out ZoneState zone));
            Assert.AreEqual("Modified Name", zone.Name);
            Assert.AreEqual("#654321", zone.ColorHex);
            Assert.AreEqual(expandedCells.Count, zone.Cells.Count);
            Assert.AreEqual(6, zone.Spots.Count);

            (int amount, _, _) = loadedZones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(10, amount);
        }

        #endregion
    }
}
