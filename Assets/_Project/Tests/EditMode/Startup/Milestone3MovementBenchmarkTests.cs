using System;
using System.Diagnostics;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.DemoHunger;
using TheEmptying.Modules.MapMovement;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>
    /// Explicit performance benchmark for prompt 3.3, following the same
    /// shape as the milestone 1 and milestone 2 benchmarks: 500 movers
    /// walking to random destinations (re-sent the moment they arrive, so
    /// they stay active for the whole run) for one in-game day. Reports
    /// average and worst ms per tick after a warm-up tick; fails only on a
    /// wildly blown budget.
    /// </summary>
    public class Milestone3MovementBenchmarkTests
    {
        private const int MoverCount = 500;
        private const double AverageTickBudgetMs = 4.0;

        [Test]
        public void FiveHundredMovers_RandomDestinationsForOneInGameDay_StaysWithinBudget()
        {
            using var core = new TempCoreContentCopy();
            var module = new MapMovementModule();
            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { module, new DemoHungerModule() });

            simulation.Events.Subscribe(MapMovementModule.MoverArrivedEventId, e =>
            {
                EntityId mover = e.Data.GetEntity("mover");
                module.TryMoveToRandomDestination(mover);
            });

            for (int i = 0; i < MoverCount; i++)
            {
                EntityId mover = module.SpawnMover();
                module.TryMoveToRandomDestination(mover);
            }

            long tickCount = CalendarConstants.TicksPerDay;

            // One untimed tick first, so first-call JIT and one-time setup don't pollute the measured window.
            simulation.Step();
            tickCount -= 1;

            var tickElapsedStopwatchTicks = new long[tickCount];

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

            long previousTimestamp = Stopwatch.GetTimestamp();
            for (long i = 0; i < tickCount; i++)
            {
                simulation.Step();
                long now = Stopwatch.GetTimestamp();
                tickElapsedStopwatchTicks[i] = now - previousTimestamp;
                previousTimestamp = now;
            }

            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            double msPerStopwatchTick = 1000.0 / Stopwatch.Frequency;
            double totalMs = 0.0;
            double worstMs = 0.0;
            for (long i = 0; i < tickCount; i++)
            {
                double ms = tickElapsedStopwatchTicks[i] * msPerStopwatchTick;
                totalMs += ms;
                if (ms > worstMs)
                {
                    worstMs = ms;
                }
            }

            double averageMs = totalMs / tickCount;
            long totalAllocatedBytes = allocatedAfter - allocatedBefore;
            double bytesPerTick = (double)totalAllocatedBytes / tickCount;

            TestContext.WriteLine($"Movers: {MoverCount}, ticks measured: {tickCount}");
            TestContext.WriteLine($"Average tick: {averageMs:F4} ms");
            TestContext.WriteLine($"Worst tick: {worstMs:F4} ms");
            TestContext.WriteLine($"Total GC allocations: {totalAllocatedBytes:N0} bytes ({bytesPerTick:F1} bytes/tick)");

            Assert.Less(averageMs, AverageTickBudgetMs, $"Average tick time {averageMs:F4} ms exceeded the {AverageTickBudgetMs} ms budget.");
        }
    }
}
