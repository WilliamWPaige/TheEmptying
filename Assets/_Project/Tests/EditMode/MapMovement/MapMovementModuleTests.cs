using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Gathering;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.Jobs;
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Market;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.MapMovement
{
    public class MapMovementModuleTests
    {
        private static GridPoint CurrentCell(MapMovementModule module, EntityId mover)
        {
            foreach (KeyValuePair<EntityId, MoverState> row in module.Movers)
            {
                if (row.Key == mover)
                {
                    return module.Grid.WorldToCell(row.Value.X, row.Value.Y);
                }
            }

            throw new KeyNotFoundException($"No mover state for {mover}.");
        }

        private static MoverState GetState(MapMovementModule module, EntityId mover)
        {
            foreach (KeyValuePair<EntityId, MoverState> row in module.Movers)
            {
                if (row.Key == mover)
                {
                    return row.Value;
                }
            }

            throw new KeyNotFoundException($"No mover state for {mover}.");
        }

        private static (Simulation Simulation, MapMovementModule Module) Build(TempCoreContentCopy core, ulong seed = 1)
        {
            var module = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(module, inventory);
            var wealth = new WealthModule(people);
            var land = new LandModule(module, wealth);
            var buildings = new BuildingsModule(module, land, wealth, inventory, people);
            var jobs = new JobsModule(buildings, wealth, people, module);
            var market = new MarketModule(buildings, wealth, inventory, people, module);
            var gathering = new GatheringModule(people, buildings, jobs, inventory, market, module);
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, new IModule[] { module, inventory, people, wealth, land, buildings, jobs, market, gathering }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, module);
        }

        [Test]
        public void Mover_AdvancesAtTheConfiguredWalkingSpeed_AndArrivesAfterExactlyTheExpectedTicks()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapMovementModule module) = Build(core);

            // 3 meters per tick at the default 3-meter cell gives exactly 1 cell per tick.
            sim.Settings.Set(MapMovementModule.WalkingSpeedMetersPerTickSettingId, 3.0);

            EntityId mover = module.SpawnMover();
            GridPoint start = CurrentCell(module, mover);
            var goal = new GridPoint(start.X + 5, start.Y);

            int arrivedCount = 0;
            sim.Events.Subscribe(MapMovementModule.MoverArrivedEventId, _ => arrivedCount++);

            module.TryMoveTo(mover, goal);

            for (int i = 0; i < 4; i++)
            {
                sim.Step();
                Assert.AreEqual(0, arrivedCount, $"Should not have arrived yet after tick {i + 1}.");
            }

            sim.Step();
            Assert.AreEqual(1, arrivedCount);
            Assert.AreEqual(goal, CurrentCell(module, mover));
        }

        /// <summary>
        /// 3.9g: the old walking speed (5 km/h, converted straight through sim ticks) worked out to
        /// about 167 m per real second once the real-time tick rate was applied, which looked like
        /// teleporting. The replacement setting is authored directly in meters per tick instead, so
        /// this checks the shipped default still lands where the design doc says: about 3 cells (9 m)
        /// per real second at 1x, and a cross-map walk (about 3 in-game hours) taking on the order of
        /// 80-90 real seconds. GameBootstrap's real-time pacing (720 real seconds per in-game day at
        /// 1x - 12 real minutes/day) is duplicated here as a plain number: this test checks the
        /// content default against that documented assumption, not a reference to the presentation layer.
        /// </summary>
        [Test]
        public void DefaultWalkingSpeed_MatchesTheDesignedRealTimePacing()
        {
            const double realSecondsPerDayAt1x = 720.0;

            using var core = new TempCoreContentCopy();
            (Simulation sim, MapMovementModule module) = Build(core);

            double metersPerTick = sim.Settings.Get(MapMovementModule.WalkingSpeedMetersPerTickSettingId);
            double cellSizeMeters = sim.Settings.Get(MapMovementModule.CellSizeMetersSettingId);
            double ticksPerRealSecondAt1x = CalendarConstants.TicksPerDay / realSecondsPerDayAt1x;

            double cellsPerRealSecondAt1x = metersPerTick * ticksPerRealSecondAt1x / cellSizeMeters;
            Assert.AreEqual(3.0, cellsPerRealSecondAt1x, 0.1, "Expected about 3 cells per real second at 1x.");

            double crossMapTicks = CalendarConstants.TicksPerHour * 3; // the design doc's "about 3 in-game hours"
            double crossMapRealSeconds = crossMapTicks / ticksPerRealSecondAt1x;
            Assert.GreaterOrEqual(crossMapRealSeconds, 80.0);
            Assert.LessOrEqual(crossMapRealSeconds, 90.0);
        }

        [Test]
        public void Mover_WithNoPathToAnUnwalkableGoal_PostsNoPathEventExactlyOnce()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapMovementModule module) = Build(core);

            EntityId mover = module.SpawnMover();
            GridPoint start = CurrentCell(module, mover);
            var goal = new GridPoint(start.X + 10, start.Y);
            module.Grid.SetWalkable(goal, false);

            int noPathCount = 0;
            sim.Events.Subscribe(MapMovementModule.MoverNoPathEventId, _ => noPathCount++);

            module.TryMoveTo(mover, goal);
            sim.Step();

            Assert.AreEqual(1, noPathCount);
        }

        [Test]
        public void PerTickSearchLimit_IsRespected()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapMovementModule module) = Build(core);

            sim.Settings.Set(MapMovementModule.MaxPathSearchesPerTickSettingId, 2);

            for (int i = 0; i < 5; i++)
            {
                EntityId mover = module.SpawnMover();
                module.TryMoveTo(mover, new GridPoint(200, 200));
            }

            Assert.AreEqual(5, module.PendingRequestCount);

            sim.Step();
            Assert.AreEqual(3, module.PendingRequestCount, "Only 2 of the 5 queued requests should process in one tick.");

            sim.Step();
            Assert.AreEqual(1, module.PendingRequestCount);

            sim.Step();
            Assert.AreEqual(0, module.PendingRequestCount);
        }

        [Test]
        public void Requests_AreProcessedInTheOrderTheyWereQueued()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapMovementModule module) = Build(core);

            sim.Settings.Set(MapMovementModule.MaxPathSearchesPerTickSettingId, 1);

            var movers = new List<EntityId>();
            for (int i = 0; i < 3; i++)
            {
                movers.Add(module.SpawnMover());
            }

            var arrivalOrder = new List<EntityId>();
            sim.Events.Subscribe(MapMovementModule.MoverArrivedEventId, e => arrivalOrder.Add(e.Data.GetEntity("mover")));

            // Each mover's "destination" is its own current cell, so the request
            // resolves to an immediate arrival the instant it's processed.
            foreach (EntityId mover in movers)
            {
                module.TryMoveTo(mover, CurrentCell(module, mover));
            }

            sim.Step();
            sim.Step();
            sim.Step();

            CollectionAssert.AreEqual(movers, arrivalOrder);
        }

        [Test]
        public void NewlyBlockedCell_TriggersANewPathRequest()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapMovementModule module) = Build(core);
            sim.Settings.Set(MapMovementModule.WalkingSpeedMetersPerTickSettingId, 3.0); // 1 cell per tick

            EntityId mover = module.SpawnMover();
            GridPoint start = CurrentCell(module, mover);
            var goal = new GridPoint(start.X + 5, start.Y);

            module.TryMoveTo(mover, goal);
            sim.Step(); // resolves the path and takes the first step

            MoverState state = GetState(module, mover);
            Assert.Greater(state.Path.Count, 0);
            GridPoint nextCell = state.Path.Peek();
            module.Grid.SetWalkable(nextCell, false);

            int pendingBefore = module.PendingRequestCount;
            sim.Step();

            Assert.AreEqual(0, state.Path.Count, "The blocked path should have been dropped.");
            Assert.Greater(module.PendingRequestCount, pendingBefore);
        }
    }
}
