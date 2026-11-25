using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.Ponds;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Tests.EditMode.Modules
{
    /// <summary>R17: pond size/capacity/refill ranges, the dry state, the guaranteed starting pond, founding clears ponds, and the save/load round trip - all against the real shipped content.</summary>
    public class PondsModuleTests
    {
        private static (Simulation Simulation, MapModule Map, PondsModule Ponds) BuildNewGame(TempCoreContentCopy core, ulong seed = 1)
        {
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, modules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (
                simulation,
                (MapModule)modules.First(m => m is MapModule),
                (PondsModule)modules.First(m => m is PondsModule));
        }

        [TestCase(PondsModule.PondsPerChunkSettingId, SettingValueType.Integer, 1.0, 0.0, 20.0, "Ponds")]
        [TestCase(PondsModule.PondMinRadiusMetersSettingId, SettingValueType.Number, 1.5, 0.5, 20.0, "Ponds")]
        [TestCase(PondsModule.PondMaxRadiusMetersSettingId, SettingValueType.Number, 4.0, 0.5, 20.0, "Ponds")]
        [TestCase(PondsModule.PondMaxDrinksSettingId, SettingValueType.Integer, 20.0, 1.0, 1000.0, "Ponds")]
        [TestCase(PondsModule.PondRefillDrinksPerHourSettingId, SettingValueType.Integer, 1.0, 0.0, 100.0, "Ponds")]
        [TestCase("setting.ponds.path_cost_multiplier", SettingValueType.Number, 10.0, 1.0, 1000.0, "Ponds")]
        [TestCase(PondsModule.StartingPondMinDistanceMetersSettingId, SettingValueType.Number, 20.0, 0.0, 1000.0, "Ponds")]
        [TestCase(PondsModule.StartingPondMaxDistanceMetersSettingId, SettingValueType.Number, 50.0, 0.0, 1000.0, "Ponds")]
        [TestCase(PondsModule.ClearMarginCellsSettingId, SettingValueType.Integer, 1.0, 0.0, 10.0, "Ponds")]
        public void Setting_LoadsFromRealContent_WithExpectedDefaultRangeAndCategory(
            string settingId, SettingValueType expectedType, double expectedDefault, double expectedMinimum, double expectedMaximum, string expectedCategory)
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, _) = BuildNewGame(core);

            Assert.IsTrue(simulation.Settings.TryGetDefinition(settingId, out SettingDefinition definition), $"Expected '{settingId}' to be declared.");
            Assert.AreEqual(expectedType, definition.ValueType);
            Assert.AreEqual(expectedDefault, definition.Default);
            Assert.AreEqual(expectedMinimum, definition.Minimum);
            Assert.AreEqual(expectedMaximum, definition.Maximum);
            Assert.AreEqual(expectedCategory, definition.Category);
            Assert.AreEqual(expectedDefault, simulation.Settings.Get(settingId), "The live value should start at the default.");
        }

        [TestCase(MapModule.TreesPerChunkSettingId, 60.0)]
        [TestCase(MapModule.StonesPerChunkSettingId, 5.0)]
        public void R17_DensitySettings_HaveTheirRaisedDefaults(string settingId, double expectedDefault)
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, _, _) = BuildNewGame(core);

            Assert.AreEqual(expectedDefault, simulation.Settings.Get(settingId));
        }

        /// <summary>Every pond generated for a chunk starts full, with its radius inside the min/max placeholder range.</summary>
        [Test]
        public void GeneratedPonds_RadiusAndDrinksStartInRange()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            double minRadius = simulation.Settings.Get(PondsModule.PondMinRadiusMetersSettingId);
            double maxRadius = simulation.Settings.Get(PondsModule.PondMaxRadiusMetersSettingId);
            int maxDrinks = (int)simulation.Settings.Get(PondsModule.PondMaxDrinksSettingId);

            bool sawAny = false;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    foreach (EntityId id in ponds.GetOrCreatePondsForChunk(new ChunkCoord(5 + dx, 9 + dz)))
                    {
                        sawAny = true;
                        Assert.IsTrue(ponds.TryGetPond(id, out PondState pond));
                        Assert.That(pond.RadiusMeters, Is.InRange(minRadius, maxRadius));
                        Assert.AreEqual(maxDrinks, pond.DrinksCurrent);
                        Assert.IsFalse(pond.IsDry);
                    }
                }
            }

            Assert.IsTrue(sawAny, "Sanity check: expected at least one generated pond nearby.");
        }

        /// <summary>R17: exactly one pond always exists 20-50 m from the starting site, regardless of the per-chunk random scatter.</summary>
        [Test]
        public void GuaranteedStartingPond_ExistsAndIsWithinRangeOfTheStartingSite()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            var sites = BuiltInContentTypes.StartingSites(simulation.Content);
            Assert.Greater(sites.Count, 0);
            double siteX = map.WorldWidthMeters * sites[0].FractionX;
            double siteZ = map.WorldDepthMeters * sites[0].FractionZ;

            double minDistance = simulation.Settings.Get(PondsModule.StartingPondMinDistanceMetersSettingId);
            double maxDistance = simulation.Settings.Get(PondsModule.StartingPondMaxDistanceMetersSettingId);

            bool foundOneInRange = ponds.Ponds.Any(row =>
            {
                double dx = row.Value.CenterX - siteX;
                double dz = row.Value.CenterZ - siteZ;
                double distance = System.Math.Sqrt(dx * dx + dz * dz);
                return distance >= minDistance - 0.001 && distance <= maxDistance + 0.001;
            });

            Assert.IsTrue(foundOneInRange, "Expected a guaranteed pond within the placeholder distance range of the starting site.");
        }

        /// <summary>R17: founding removes whatever a building's footprint overlaps, leaves every other pond untouched, and the removal stays exactly as it was after a save and load.</summary>
        [Test]
        public void RemovePondsOverlapping_RemovesOnlyTheOverlappingPonds_AndStaysRemovedAfterSaveAndLoad()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            var coord = new ChunkCoord(5, 9);
            System.Collections.Generic.List<EntityId> before = ponds.GetOrCreatePondsForChunk(coord).ToList();
            Assert.Greater(before.Count, 0, "Sanity check: expected at least one pond in this chunk.");

            Assert.IsTrue(ponds.TryGetPond(before[0], out PondState target));
            double cellSize = map.CellSizeMeters;
            CellCoord targetCell = map.WorldToCell(target.CenterX, target.CenterZ);
            var min = new CellCoord(targetCell.X - 1, targetCell.Z - 1);
            var maxExclusive = new CellCoord(targetCell.X + 2, targetCell.Z + 2);

            ponds.RemovePondsOverlapping(min, maxExclusive);

            Assert.IsFalse(ponds.TryGetPond(before[0], out PondState _), "The overlapping pond should be gone.");

            System.Collections.Generic.List<EntityId> survivors = before.Skip(1).ToList();
            foreach (EntityId survivorId in survivors)
            {
                Assert.IsTrue(ponds.TryGetPond(survivorId, out PondState _), "A non-overlapping pond must survive.");
            }

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var loadedPonds = (PondsModule)loadedModules.First(m => m is PondsModule);

            Assert.IsFalse(loadedPonds.TryGetPond(before[0], out PondState _), "The removal must survive a save and load.");
            foreach (EntityId survivorId in survivors)
            {
                Assert.IsTrue(loadedPonds.TryGetPond(survivorId, out PondState _));
            }
        }

        /// <summary>R17: each drink taken removes exactly 1, down to (and never below) 0.</summary>
        [Test]
        public void Drink_TakesExactlyOneDrink_AndNeverGoesNegative()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            EntityId pondId = ponds.Ponds.First().Key;
            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState pond));
            int startingDrinks = pond.DrinksCurrent;

            ponds.Drink(pondId);
            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState afterOneDrink));
            Assert.AreEqual(startingDrinks - 1, afterOneDrink.DrinksCurrent);

            for (int i = 0; i < startingDrinks + 5; i++)
            {
                ponds.Drink(pondId);
            }

            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState afterMany));
            Assert.AreEqual(0, afterMany.DrinksCurrent);
            Assert.IsTrue(afterMany.IsDry);
        }

        /// <summary>R17: at 0 drinks a pond is dry, and it refills back up over game time, never past the max.</summary>
        [Test]
        public void ADryPond_RefillsOverTime_NeverPastMax()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            EntityId pondId = ponds.Ponds.First().Key;
            int maxDrinks = (int)simulation.Settings.Get(PondsModule.PondMaxDrinksSettingId);
            int refillPerHour = (int)simulation.Settings.Get(PondsModule.PondRefillDrinksPerHourSettingId);
            Assert.Greater(refillPerHour, 0, "Sanity check for this test's real shipped settings.");

            ponds.SetDrinks(pondId, 0);
            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState dry));
            Assert.IsTrue(dry.IsDry);

            for (int tick = 0; tick < CalendarConstants.TicksPerHour * 5; tick++)
            {
                simulation.Step();
            }

            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState afterFiveHours));
            Assert.Greater(afterFiveHours.DrinksCurrent, 0, "A dry pond should have refilled at least some over 5 game hours.");
            Assert.LessOrEqual(afterFiveHours.DrinksCurrent, maxDrinks);

            ponds.SetDrinks(pondId, maxDrinks);
            for (int tick = 0; tick < CalendarConstants.TicksPerHour * 3; tick++)
            {
                simulation.Step();
            }

            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState stillFull));
            Assert.AreEqual(maxDrinks, stillFull.DrinksCurrent, "A full pond must never refill past its max.");
        }

        /// <summary>R17: the "Empty pond"/"Fill pond" cheats clamp to [0, max drinks].</summary>
        [Test]
        public void SetDrinks_ClampsToZeroAndMax()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            EntityId pondId = ponds.Ponds.First().Key;
            int maxDrinks = (int)simulation.Settings.Get(PondsModule.PondMaxDrinksSettingId);

            ponds.SetDrinks(pondId, -5);
            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState afterNegative));
            Assert.AreEqual(0, afterNegative.DrinksCurrent);

            ponds.SetDrinks(pondId, maxDrinks + 50);
            Assert.IsTrue(ponds.TryGetPond(pondId, out PondState afterOverMax));
            Assert.AreEqual(maxDrinks, afterOverMax.DrinksCurrent);
        }

        /// <summary>R17: every pond's position, size and drinks, and which chunks have already been generated (including the guaranteed one, whose own chunk's random scatter hasn't necessarily run yet), survive a save and load round trip exactly.</summary>
        [Test]
        public void SaveAndLoad_PondsAndGenerationState_SurviveTheRoundTrip()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule pondsBefore) = BuildNewGame(core);

            // Force a few chunks (beyond whatever the guaranteed pond already touched) to generate before saving.
            var coords = new[] { new ChunkCoord(5, 9), new ChunkCoord(6, 9), new ChunkCoord(5, 10) };
            foreach (ChunkCoord coord in coords)
            {
                pondsBefore.GetOrCreatePondsForChunk(coord);
            }

            EntityId mutatedId = pondsBefore.Ponds.First().Key;
            pondsBefore.SetDrinks(mutatedId, 3);

            System.Collections.Generic.List<(EntityId Id, double CenterX, double CenterZ, double RadiusMeters, int DrinksCurrent)> before =
                pondsBefore.Ponds.Select(r => (r.Key, r.Value.CenterX, r.Value.CenterZ, r.Value.RadiusMeters, r.Value.DrinksCurrent)).ToList();

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var pondsAfter = (PondsModule)loadedModules.First(m => m is PondsModule);

            System.Collections.Generic.List<(EntityId Id, double CenterX, double CenterZ, double RadiusMeters, int DrinksCurrent)> after =
                pondsAfter.Ponds.Select(r => (r.Key, r.Value.CenterX, r.Value.CenterZ, r.Value.RadiusMeters, r.Value.DrinksCurrent)).ToList();

            Assert.AreEqual(before.Count, after.Count);
            CollectionAssert.AreEquivalent(before, after);

            // Re-querying an already-generated chunk after load must not create any new ponds (it must still be marked generated, not re-rolled).
            foreach (ChunkCoord coord in coords)
            {
                int countBefore = pondsAfter.Ponds.Count();
                pondsAfter.GetOrCreatePondsForChunk(coord);
                Assert.AreEqual(countBefore, pondsAfter.Ponds.Count(), $"Chunk {coord} should already have been marked generated.");
            }
        }

        /// <summary>R17b: nothing (tree, stone or grub mound) may stand on a pond's water or within the clear-margin setting of its edge - checked against every pond, in every chunk asked about, for several chunks.</summary>
        private static void AssertNoPlacementWithinPondMargins(MapModule map, IReadOnlyList<PondState> ponds, IEnumerable<ChunkCoord> chunks, double marginMeters)
        {
            double cellSize = map.CellSizeMeters;

            foreach (ChunkCoord coord in chunks)
            {
                ChunkData chunk = map.GetOrCreateChunk(coord);
                foreach (ChunkPlacement placement in chunk.Placements)
                {
                    int footprint = placement.Kind == PlacementKind.Stone ? 2 : 1;
                    double rectMinX = placement.Cell.X * cellSize;
                    double rectMinZ = placement.Cell.Z * cellSize;
                    double rectMaxX = rectMinX + footprint * cellSize;
                    double rectMaxZ = rectMinZ + footprint * cellSize;

                    foreach (PondState pond in ponds)
                    {
                        double closestX = System.Math.Clamp(pond.CenterX, rectMinX, rectMaxX);
                        double closestZ = System.Math.Clamp(pond.CenterZ, rectMinZ, rectMaxZ);
                        double dx = pond.CenterX - closestX;
                        double dz = pond.CenterZ - closestZ;
                        double distance = System.Math.Sqrt(dx * dx + dz * dz);

                        Assert.GreaterOrEqual(
                            distance,
                            pond.RadiusMeters + marginMeters - 1e-9,
                            $"{placement.Kind} at {placement.Cell} (chunk {coord}) is within the clear margin of a pond centered at ({pond.CenterX:0.##}, {pond.CenterZ:0.##}), radius {pond.RadiusMeters:0.##}.");
                    }
                }
            }
        }

        /// <summary>R17b: a pond's own random scatter never leaves a tree, stone or mound on its water or within one margin cell of its edge - checked across several chunks.</summary>
        [Test]
        public void GeneratedPonds_KeepMapPlacementsClearOfWaterAndTheMargin()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            double marginMeters = simulation.Settings.Get(PondsModule.ClearMarginCellsSettingId) * map.CellSizeMeters;

            var checkedChunks = new List<ChunkCoord>();
            for (int cx = 3; cx <= 7; cx++)
            {
                for (int cz = 7; cz <= 11; cz++)
                {
                    var coord = new ChunkCoord(cx, cz);
                    ponds.GetOrCreatePondsForChunk(coord);
                    checkedChunks.Add(coord);
                }
            }

            AssertNoPlacementWithinPondMargins(map, ponds.Ponds.Select(r => r.Value).ToList(), checkedChunks, marginMeters);
        }

        /// <summary>R17b: same as above, but for a different world seed - the clearing rule must not depend on which seed happens to be running.</summary>
        [Test]
        public void GeneratedPonds_KeepMapPlacementsClearOfWaterAndTheMargin_OnADifferentSeed()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core, seed: 99);

            double marginMeters = simulation.Settings.Get(PondsModule.ClearMarginCellsSettingId) * map.CellSizeMeters;

            var checkedChunks = new List<ChunkCoord>();
            for (int cx = 10; cx <= 14; cx++)
            {
                for (int cz = 2; cz <= 6; cz++)
                {
                    var coord = new ChunkCoord(cx, cz);
                    ponds.GetOrCreatePondsForChunk(coord);
                    checkedChunks.Add(coord);
                }
            }

            AssertNoPlacementWithinPondMargins(map, ponds.Ponds.Select(r => r.Value).ToList(), checkedChunks, marginMeters);
        }

        /// <summary>R17b: the guaranteed starting pond clears its own water and margin too, not just scattered ponds.</summary>
        [Test]
        public void GuaranteedStartingPond_AlsoKeepsMapPlacementsClear()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule map, PondsModule ponds) = BuildNewGame(core);

            PondState guaranteed = ponds.Ponds.Select(r => r.Value).Single();
            double marginMeters = simulation.Settings.Get(PondsModule.ClearMarginCellsSettingId) * map.CellSizeMeters;
            double clearRadius = guaranteed.RadiusMeters + marginMeters;

            var touchedChunks = new[]
            {
                map.WorldToChunk(guaranteed.CenterX - clearRadius, guaranteed.CenterZ - clearRadius),
                map.WorldToChunk(guaranteed.CenterX + clearRadius, guaranteed.CenterZ - clearRadius),
                map.WorldToChunk(guaranteed.CenterX - clearRadius, guaranteed.CenterZ + clearRadius),
                map.WorldToChunk(guaranteed.CenterX + clearRadius, guaranteed.CenterZ + clearRadius)
            }.Distinct().ToList();

            AssertNoPlacementWithinPondMargins(map, new List<PondState> { guaranteed }, touchedChunks, marginMeters);
        }

        /// <summary>R17b: the same chunk, from the same seed, generates the same placements and ponds every time - the clearing doesn't introduce any order or visit-time dependence.</summary>
        [Test]
        public void PondClearing_SameSeedAndChunk_AlwaysGivesTheSamePlacementsAndPonds()
        {
            using var coreA = new TempCoreContentCopy();
            using var coreB = new TempCoreContentCopy();
            (Simulation _, MapModule mapA, PondsModule pondsA) = BuildNewGame(coreA, 5);
            (Simulation _, MapModule mapB, PondsModule pondsB) = BuildNewGame(coreB, 5);

            var coord = new ChunkCoord(5, 9);
            List<EntityId> pondIdsA = pondsA.GetOrCreatePondsForChunk(coord).ToList();
            List<EntityId> pondIdsB = pondsB.GetOrCreatePondsForChunk(coord).ToList();
            Assert.AreEqual(pondIdsA.Count, pondIdsB.Count);

            for (int i = 0; i < pondIdsA.Count; i++)
            {
                Assert.IsTrue(pondsA.TryGetPond(pondIdsA[i], out PondState pondA));
                Assert.IsTrue(pondsB.TryGetPond(pondIdsB[i], out PondState pondB));
                Assert.AreEqual(pondA.CenterX, pondB.CenterX);
                Assert.AreEqual(pondA.CenterZ, pondB.CenterZ);
                Assert.AreEqual(pondA.RadiusMeters, pondB.RadiusMeters);
            }

            List<ChunkPlacement> placementsA = mapA.GetOrCreateChunk(coord).Placements.ToList();
            List<ChunkPlacement> placementsB = mapB.GetOrCreateChunk(coord).Placements.ToList();

            Assert.AreEqual(placementsA.Count, placementsB.Count);
            for (int i = 0; i < placementsA.Count; i++)
            {
                Assert.AreEqual(placementsA[i].Kind, placementsB[i].Kind);
                Assert.AreEqual(placementsA[i].Cell, placementsB[i].Cell);
            }
        }

        /// <summary>R17b: the cleared-out state (both the pond and the map placements it removed) survives a save and load round trip exactly - the invariant still holds afterward too.</summary>
        [Test]
        public void PondClearing_SurvivesSaveAndLoad()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, MapModule mapBefore, PondsModule pondsBefore) = BuildNewGame(core);

            var coords = new[] { new ChunkCoord(5, 9), new ChunkCoord(6, 9), new ChunkCoord(5, 10) };
            foreach (ChunkCoord coord in coords)
            {
                pondsBefore.GetOrCreatePondsForChunk(coord);
            }

            double marginMeters = simulation.Settings.Get(PondsModule.ClearMarginCellsSettingId) * mapBefore.CellSizeMeters;
            AssertNoPlacementWithinPondMargins(mapBefore, pondsBefore.Ponds.Select(r => r.Value).ToList(), coords, marginMeters);

            using var stream = new MemoryStream();
            GameSave.Save(simulation, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            IModule[] loadedModules = ModuleCatalog.CreateModules();
            GameSave.Load(stream, new[] { core.FolderPath }, loadedModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            var mapAfter = (MapModule)loadedModules.First(m => m is MapModule);
            var pondsAfter = (PondsModule)loadedModules.First(m => m is PondsModule);

            AssertNoPlacementWithinPondMargins(mapAfter, pondsAfter.Ponds.Select(r => r.Value).ToList(), coords, marginMeters);
        }
    }
}
