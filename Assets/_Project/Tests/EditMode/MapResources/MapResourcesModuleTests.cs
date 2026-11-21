using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.MapResources;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.MapResources
{
    /// <summary>3.10a: trees and rock outcrops placed near the keep, each with a finite stock.</summary>
    public class MapResourcesModuleTests
    {
        private static (Simulation Simulation, MapResourcesModule MapResources, LandModule Land, MapMovementModule MapMovement) Build(TempCoreContentCopy core, ulong seed = 1)
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
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath },
                new IModule[] { mapMovement, inventory, people, wealth, land, buildings, jobs, market, mapResources, gathering },
                contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, mapResources, land, mapMovement);
        }

        [Test]
        public void NewGame_PlacesTheTestLayout_NearTheKeep_OutsideTheCrownGrounds()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapResourcesModule mapResources, LandModule land, _) = Build(core);

            var trees = mapResources.Nodes.Where(n => n.Value.Kind == ResourceNodeKind.Tree).ToList();
            var outcrops = mapResources.Nodes.Where(n => n.Value.Kind == ResourceNodeKind.Outcrop).ToList();

            int expectedForests = (int)sim.Settings.Get(MapResourcesModule.ForestCountSettingId);
            int expectedTreesPerForest = (int)sim.Settings.Get(MapResourcesModule.TreesPerForestSettingId);
            int expectedOutcrops = (int)sim.Settings.Get(MapResourcesModule.OutcropCountSettingId);
            int radius = (int)sim.Settings.Get(MapResourcesModule.PlacementRadiusCellsSettingId);
            int clusterRadius = (int)sim.Settings.Get(MapResourcesModule.ForestClusterRadiusCellsSettingId);

            Assert.AreEqual(expectedForests * expectedTreesPerForest, trees.Count, "Expected 3 forests of about 20 trees each.");
            Assert.AreEqual(expectedOutcrops, outcrops.Count, "Expected 2 outcrops.");

            GridPoint keepArea = land.CrownGrounds.Center;
            // Both dx and dy are independently rolled within [-radius, radius] (or [-clusterRadius,
            // clusterRadius] for a tree around its forest's own center), so the worst-case Manhattan
            // distance from either roll is 2x the radius, not the radius itself.
            int maxDistance = 2 * (radius + clusterRadius);

            foreach (var row in mapResources.Nodes)
            {
                Assert.IsFalse(land.CrownGrounds.Contains(row.Value.Cell), $"{row.Key} should not overlap the crown grounds.");
                int distance = System.Math.Abs(row.Value.Cell.X - keepArea.X) + System.Math.Abs(row.Value.Cell.Y - keepArea.Y);
                Assert.LessOrEqual(distance, maxDistance, $"{row.Key} should be near the keep (within about {maxDistance} cells).");
            }
        }

        [Test]
        public void ATree_YieldsFiveLogs_ThenDisappears()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapResourcesModule mapResources, _, _) = Build(core);

            EntityId treeId = mapResources.Nodes.First(n => n.Value.Kind == ResourceNodeKind.Tree).Key;
            int expectedYield = (int)sim.Settings.Get(MapResourcesModule.LogsPerTreeSettingId);

            int totalHarvested = 0;
            for (int i = 0; i < expectedYield + 2; i++)
            {
                totalHarvested += mapResources.Harvest(treeId, 1);
            }

            Assert.AreEqual(expectedYield, totalHarvested, "Expected exactly the tree's full yield, no more.");
            Assert.IsFalse(mapResources.TryGetNode(treeId, out _), "Expected the tree to be gone once fully harvested.");
        }

        [Test]
        public void AnOutcrop_Holds300Stone_ThenDisappears()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapResourcesModule mapResources, _, _) = Build(core);

            EntityId outcropId = mapResources.Nodes.First(n => n.Value.Kind == ResourceNodeKind.Outcrop).Key;
            int expectedYield = (int)sim.Settings.Get(MapResourcesModule.StonePerOutcropSettingId);

            int harvested = mapResources.Harvest(outcropId, expectedYield + 50);

            Assert.AreEqual(expectedYield, harvested, "A single harvest call should still be capped at what's actually left.");
            Assert.IsFalse(mapResources.TryGetNode(outcropId, out _));
        }

        [Test]
        public void TryFindNearest_ExcludesClaimedCells_AndIgnoresTheOtherKind()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapResourcesModule mapResources, _, _) = Build(core);

            var trees = mapResources.Nodes.Where(n => n.Value.Kind == ResourceNodeKind.Tree).ToList();
            Assert.Greater(trees.Count, 1, "Setup: expected more than one tree.");

            GridPoint from = trees[0].Value.Cell;
            Assert.IsTrue(mapResources.TryFindNearest(ResourceNodeKind.Tree, from, null, out EntityId nearestId, out GridPoint nearestCell));
            Assert.AreEqual(trees[0].Key, nearestId, "Setup: the tree's own cell should be its own nearest tree.");

            var excluding = new System.Collections.Generic.HashSet<GridPoint> { nearestCell };
            Assert.IsTrue(mapResources.TryFindNearest(ResourceNodeKind.Tree, from, excluding, out EntityId secondNearestId, out _));
            Assert.AreNotEqual(nearestId, secondNearestId, "Expected a claimed cell to be skipped in favor of the next-nearest tree.");

            bool foundOutcrop = mapResources.TryFindNearest(ResourceNodeKind.Outcrop, from, null, out EntityId outcropId, out _);
            Assert.IsTrue(foundOutcrop, "Expected an outcrop to be found when asked for that kind specifically.");
            Assert.AreNotEqual(nearestId, outcropId, "A Tree search and an Outcrop search should never return the same node.");
        }

        [Test]
        public void TreesAndOutcrops_ArePassable_NotObstaclesToMovementOrPlacement()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapResourcesModule mapResources, _, MapMovementModule mapMovement) = Build(core);

            // 3.10a: a node that blocked its own cell would be permanently unreachable - nothing
            // could ever stand on it to harvest it. Confirms the deliberate choice: passable.
            foreach (var row in mapResources.Nodes)
            {
                Assert.IsTrue(mapMovement.Grid.IsPassable(row.Value.Cell), $"{row.Key}'s own cell should be passable.");
            }
        }

        [Test]
        public void SaveAndLoad_RemainingStockAndRemovedTrees_MatchesAnUnbrokenRun()
        {
            using var core = new TempCoreContentCopy();
            const ulong seed = 777;

            (Simulation uninterrupted, MapResourcesModule uninterruptedResources, _, _) = Build(core, seed);
            EntityId uninterruptedTreeId = uninterruptedResources.Nodes.First(n => n.Value.Kind == ResourceNodeKind.Tree).Key;
            int logsPerTree = (int)uninterrupted.Settings.Get(MapResourcesModule.LogsPerTreeSettingId);
            uninterruptedResources.Harvest(uninterruptedTreeId, logsPerTree); // fully deplete and remove one tree
            uninterruptedResources.Harvest(uninterruptedResources.Nodes.First(n => n.Value.Kind == ResourceNodeKind.Tree).Key, 2); // partially harvest another
            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                uninterrupted.Step();
            }

            ulong expectedHash = uninterrupted.ComputeStateHash();

            (Simulation firstRun, MapResourcesModule firstResources, _, _) = Build(core, seed);
            EntityId firstTreeId = firstResources.Nodes.First(n => n.Value.Kind == ResourceNodeKind.Tree).Key;
            firstResources.Harvest(firstTreeId, logsPerTree);
            firstResources.Harvest(firstResources.Nodes.First(n => n.Value.Kind == ResourceNodeKind.Tree).Key, 2);

            using var stream = new MemoryStream();
            GameSave.Save(firstRun, new SaveHeaderInfo { KingdomName = "K", Population = 9 }, stream);
            stream.Position = 0;

            var mapMovement2 = new MapMovementModule();
            var inventory2 = new InventoryModule();
            var people2 = new PeopleModule(mapMovement2, inventory2);
            var wealth2 = new WealthModule(people2);
            var land2 = new LandModule(mapMovement2, wealth2);
            var buildings2 = new BuildingsModule(mapMovement2, land2, wealth2, inventory2, people2);
            var jobs2 = new JobsModule(buildings2, wealth2, people2, mapMovement2);
            var market2 = new MarketModule(buildings2, wealth2, inventory2, people2, mapMovement2);
            var mapResources2 = new MapResourcesModule(mapMovement2, land2);
            var gathering2 = new GatheringModule(people2, buildings2, jobs2, inventory2, market2, mapMovement2, mapResources2);
            IModule[] secondModules = { mapMovement2, inventory2, people2, wealth2, land2, buildings2, jobs2, market2, mapResources2, gathering2 };

            (Simulation secondRun, _) = GameSave.Load(stream, new[] { core.FolderPath }, secondModules, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            Assert.IsFalse(mapResources2.TryGetNode(firstTreeId, out _), "The fully-depleted tree should not come back after loading.");

            for (long i = 0; i < CalendarConstants.TicksPerDay; i++)
            {
                secondRun.Step();
            }

            Assert.AreEqual(expectedHash, secondRun.ComputeStateHash());
        }
    }
}
