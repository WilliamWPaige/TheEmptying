using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
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
            (Simulation simulation, _) = SimulationBuilder.Build(seed, new[] { core.FolderPath }, new IModule[] { module, inventory, people }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());
            return (simulation, module);
        }

        [Test]
        public void Mover_AdvancesAtTheConfiguredWalkingSpeed_AndArrivesAfterExactlyTheExpectedTicks()
        {
            using var core = new TempCoreContentCopy();
            (Simulation sim, MapMovementModule module) = Build(core);

            // 1.8 km/h at the default 3-meter cell gives exactly 1 cell per tick.
            sim.Settings.Set(MapMovementModule.WalkingSpeedKmhSettingId, 1.8);

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
            sim.Settings.Set(MapMovementModule.WalkingSpeedKmhSettingId, 1.8); // 1 cell per tick

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
