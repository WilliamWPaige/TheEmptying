using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>R12: slot/capacity math, overlap rules, the stockpile add logic (slots, sizes, blocks, limits) and the save/load round trip - all against the real shipped content.</summary>
    public class ZonesModuleTests
    {
        private static (Simulation Simulation, MapModule Map, BuildingsModule Buildings, ZonesModule Zones) BuildNewGame(TempCoreContentCopy core, ulong seed = 1)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (
                simulation,
                (MapModule)modules.First(m => m is MapModule),
                (BuildingsModule)modules.First(m => m is BuildingsModule),
                (ZonesModule)modules.First(m => m is ZonesModule));
        }

        #region Slot and capacity math

        [TestCase(16.0, 4)]
        [TestCase(10.0, 2)]
        [TestCase(3.9, 0)]
        [TestCase(0.0, 0)]
        public void SlotCount_IsAreaDividedByFourRoundedDown(double area, int expectedSlots)
        {
            Assert.AreEqual(expectedSlots, ZonesModule.SlotCount(area));
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
            // Size-1 item: 100 per slot. 250 of them need 3 slots (100 + 100 + 50).
            Assert.AreEqual(3, ZonesModule.SlotsNeededFor(250, itemSize: 1));
            Assert.AreEqual(1, ZonesModule.SlotsNeededFor(100, itemSize: 1));
            Assert.AreEqual(0, ZonesModule.SlotsNeededFor(0, itemSize: 1));
        }

        #endregion

        #region Placement rules

        [Test]
        public void CanPlace_RejectsARectangleSmallerThanTheMinimum()
        {
            using var core = new TempCoreContentCopy();
            (_, _, _, ZonesModule zones) = BuildNewGame(core);

            var tooSmall = new OrientedRect(100, 100, 1.5, 3, 0);
            Assert.IsFalse(zones.CanPlace(tooSmall, out string reason));
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void CanPlace_RejectsOverlappingABuilding()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, BuildingsModule buildings, ZonesModule zones) = BuildNewGame(core);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            buildings.PlaceBuilding(BuildingsModule.KeepContentId, centerX, centerZ, 12, 12);

            var overlapping = new OrientedRect(centerX, centerZ, 4, 4, 0);
            Assert.IsFalse(zones.CanPlace(overlapping, out string reason));
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void CanPlace_RejectsOverlappingAnotherZone()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, ZonesModule zones) = BuildNewGame(core);

            double centerX = map.WorldWidthMeters * 0.5;
            double centerZ = map.WorldDepthMeters * 0.5;
            var first = new OrientedRect(centerX, centerZ, 6, 6, 0);
            Assert.IsNotNull(zones.TryPlaceZone(ZonesModule.StockpileTypeId, first, out _));

            var overlapping = new OrientedRect(centerX + 2, centerZ, 6, 6, 0);
            Assert.IsFalse(zones.CanPlace(overlapping, out string reason));
            Assert.IsNotEmpty(reason);
        }

        /// <summary>Zones may cover trees, stones and grub mounds - there's deliberately no check against MapModule's placements.</summary>
        [Test]
        public void CanPlace_AllowsOverlappingMapResourcePlacements()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, ZonesModule zones) = BuildNewGame(core);

            map.GetOrCreateChunk(new ChunkCoord(5, 9)); // force the chunk (and whatever it placed) to exist first.
            double originX = 5 * map.ChunkSizeMeters;
            double originZ = 9 * map.ChunkSizeMeters;

            var rect = new OrientedRect(originX + 10, originZ + 10, 6, 6, 0);
            Assert.IsTrue(zones.CanPlace(rect, out string reason), reason);
        }

        [Test]
        public void TryPlaceZone_FailingPlacement_CreatesNoZone()
        {
            using var core = new TempCoreContentCopy();
            (_, _, _, ZonesModule zones) = BuildNewGame(core);

            var tooSmall = new OrientedRect(100, 100, 1, 1, 0);
            EntityId? id = zones.TryPlaceZone(ZonesModule.StockpileTypeId, tooSmall, out string reason);

            Assert.IsNull(id);
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(0, zones.Zones.Count());
        }

        #endregion

        #region Stockpile add logic

        private static EntityId PlaceStockpile(ZonesModule zones, double centerX, double centerZ, double sideA, double sideB)
        {
            EntityId? id = zones.TryPlaceZone(ZonesModule.StockpileTypeId, new OrientedRect(centerX, centerZ, sideA, sideB, 0), out string reason);
            Assert.IsNotNull(id, reason);
            return id.Value;
        }

        [Test]
        public void TryAddItemToStockpile_ANewStockpileStartsEmpty()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = PlaceStockpile(zones, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);

            (int amount, int limit, bool blocked) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(0, amount);
            Assert.IsFalse(blocked);
            Assert.Greater(limit, 0);
        }

        [Test]
        public void TryAddItemToStockpile_AddsUpToSlotCapacity_AndReportsWhatDidNotFit()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, ZonesModule zones) = BuildNewGame(core);

            // 4 x 4 m = 16 sq m = 4 slots; item.grub is size 1, so 100 per slot = 400 max.
            EntityId zoneId = PlaceStockpile(zones, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);

            (int added, int didNotFit, string reason) = zones.TryAddItemToStockpile(zoneId, "item.grub", 450);

            Assert.AreEqual(400, added);
            Assert.AreEqual(50, didNotFit);
            Assert.IsNotEmpty(reason);

            (int amount, _, _) = zones.GetStockpileItemInfo(zoneId, "item.grub");
            Assert.AreEqual(400, amount);
        }

        [Test]
        public void TryAddItemToStockpile_AnItemCanSpanSeveralSlots_AndLeaveRoomForAnother()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, ZonesModule zones) = BuildNewGame(core);

            // 4 slots total. 250 grubs (size 1, 100/slot) need 3 slots, leaving 1 slot (25 units) for item.log (size 4, 25/slot).
            EntityId zoneId = PlaceStockpile(zones, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);

            (int grubAdded, int grubDidNotFit, _) = zones.TryAddItemToStockpile(zoneId, "item.grub", 250);
            Assert.AreEqual(250, grubAdded);
            Assert.AreEqual(0, grubDidNotFit);

            (int logAdded, int logDidNotFit, string logReason) = zones.TryAddItemToStockpile(zoneId, "item.log", 30);
            Assert.AreEqual(25, logAdded);
            Assert.AreEqual(5, logDidNotFit);
            Assert.IsNotEmpty(logReason);
        }

        [Test]
        public void TryAddItemToStockpile_RespectsTheKeepOnHandLimit()
        {
            using var core = new TempCoreContentCopy();
            (_, MapModule map, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = PlaceStockpile(zones, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);

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
            (_, MapModule map, _, ZonesModule zones) = BuildNewGame(core);
            EntityId zoneId = PlaceStockpile(zones, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 4, 4);

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
            (Simulation simulation, MapModule map, BuildingsModule buildings, ZonesModule zones) = BuildNewGame(core);

            EntityId stockpileId = PlaceStockpile(zones, map.WorldWidthMeters * 0.5, map.WorldDepthMeters * 0.5, 6, 4);
            zones.TryAddItemToStockpile(stockpileId, "item.grub", 120);
            zones.SetKeepOnHandLimit(stockpileId, "item.grub", 300);
            zones.SetBlocked(stockpileId, "item.log", true);

            EntityId? livingAreaId = zones.TryPlaceZone(ZonesModule.LivingAreaTypeId, new OrientedRect(map.WorldWidthMeters * 0.5 + 50, map.WorldDepthMeters * 0.5, 10, 8, 37.5), out _);
            Assert.IsNotNull(livingAreaId);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var loadedZones = (ZonesModule)loadedModules.First(m => m is ZonesModule);

            Assert.AreEqual(2, loadedZones.Zones.Count());

            Assert.IsTrue(loadedZones.TryGetZone(stockpileId, out ZoneState stockpile));
            Assert.AreEqual(ZonesModule.StockpileTypeId, stockpile.ZoneTypeId);
            Assert.AreEqual(6, stockpile.SideALength);
            Assert.AreEqual(4, stockpile.SideBLength);

            (int amount, int limit, bool grubBlocked) = loadedZones.GetStockpileItemInfo(stockpileId, "item.grub");
            Assert.AreEqual(120, amount);
            Assert.AreEqual(300, limit);
            Assert.IsFalse(grubBlocked);

            (_, _, bool logBlocked) = loadedZones.GetStockpileItemInfo(stockpileId, "item.log");
            Assert.IsTrue(logBlocked);

            Assert.IsTrue(loadedZones.TryGetZone(livingAreaId.Value, out ZoneState livingArea));
            Assert.AreEqual(ZonesModule.LivingAreaTypeId, livingArea.ZoneTypeId);
            Assert.AreEqual(37.5, livingArea.RotationDegrees);
        }

        #endregion
    }
}
