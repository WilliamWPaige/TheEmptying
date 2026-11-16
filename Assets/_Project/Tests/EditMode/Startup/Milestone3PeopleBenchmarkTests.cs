using System;
using System.Diagnostics;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Inventory;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>
    /// Explicit performance benchmark for prompt 3.4: 500 people (each a
    /// mover, each with needs, mood, aging and a daily schedule) for one
    /// in-game day. Reports average and worst ms per tick after a warm-up
    /// tick; fails only on a wildly blown budget.
    /// </summary>
    public class Milestone3PeopleBenchmarkTests
    {
        private const int PersonCount = 500;
        private const double AverageTickBudgetMs = 4.0;

        [Test]
        public void FiveHundredPeople_WithMovement_OneInGameDay_StaysWithinBudget()
        {
            using var core = new TempCoreContentCopy();
            var mapMovement = new MapMovementModule();
            var inventory = new InventoryModule();
            var people = new PeopleModule(mapMovement, inventory);
            var wealth = new WealthModule(people);

            (Simulation simulation, _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, new IModule[] { mapMovement, inventory, people, wealth }, contentAssets: TempCoreContentCopy.LoadRealContentAssets());

            for (int i = 0; i < PersonCount; i++)
            {
                people.SpawnPerson(18, 45);
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

            TestContext.WriteLine($"People: {PersonCount}, ticks measured: {tickCount}");
            TestContext.WriteLine($"Average tick: {averageMs:F4} ms");
            TestContext.WriteLine($"Worst tick: {worstMs:F4} ms");
            TestContext.WriteLine($"Total GC allocations: {totalAllocatedBytes:N0} bytes ({bytesPerTick:F1} bytes/tick)");

            Assert.Less(averageMs, AverageTickBudgetMs, $"Average tick time {averageMs:F4} ms exceeded the {AverageTickBudgetMs} ms budget.");
        }
    }
}
