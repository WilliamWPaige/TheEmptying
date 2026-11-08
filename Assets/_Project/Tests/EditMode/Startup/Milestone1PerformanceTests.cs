using System;
using System.Diagnostics;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.DemoHunger;

namespace TheEmptying.Tests.EditMode.Startup
{
    /// <summary>
    /// Informational performance check for the milestone 1 build plan: 2,000
    /// entities (overriding the demo module's usual 50, for this test only)
    /// for one in-game day. Fails only on a wildly blown budget; otherwise it
    /// just reports the numbers so real optimization work has a baseline.
    /// </summary>
    public class Milestone1PerformanceTests
    {
        private const int EntityCountForThisTestOnly = 2000;
        private const double AverageTickBudgetMs = 2.0;

        [Test]
        public void DemoModule_With2000Entities_OneInGameDay_StaysWithinTheGenerousBudget()
        {
            using var core = new TempCoreContentCopy();
            string[] sources = { core.FolderPath };

            (Simulation simulation, _) = SimulationBuilder.Build(1, sources, new IModule[] { new DemoHungerModule(EntityCountForThisTestOnly) });

            long tickCount = CalendarConstants.TicksPerDay;

            // One untimed tick first, so first-call JIT and any one-time setup
            // don't pollute the measured window.
            simulation.Step();
            tickCount -= 1;

            var tickElapsedStopwatchTicks = new long[tickCount];

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);

            long previousTimestamp = Stopwatch.GetTimestamp();
            for (long i = 0; i < tickCount; i++)
            {
                simulation.Step();
                long now = Stopwatch.GetTimestamp();
                tickElapsedStopwatchTicks[i] = now - previousTimestamp;
                previousTimestamp = now;
            }

            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            int gen0After = GC.CollectionCount(0);

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
            int gen0Collections = gen0After - gen0Before;

            TestContext.WriteLine($"Entities: {EntityCountForThisTestOnly}, ticks measured: {tickCount}");
            TestContext.WriteLine($"Average tick: {averageMs:F4} ms");
            TestContext.WriteLine($"Worst tick: {worstMs:F4} ms");
            TestContext.WriteLine($"Total GC allocations: {totalAllocatedBytes:N0} bytes ({bytesPerTick:F1} bytes/tick)");
            TestContext.WriteLine($"Gen0 collections during the run: {gen0Collections}");

            if (bytesPerTick > 16.0)
            {
                TestContext.WriteLine(
                    "Allocations per tick are not near zero. Likely sources, by inspection (not profiled): " +
                    "DemoHungerModule.EntityIndices() and EntityTable<T>.Rows() are both C# iterator methods, " +
                    "so each allocates a new enumerator object every tick regardless of entity count; " +
                    "the lambda passed to BucketedWork.Run captures 'tick' and 'decayPerTick' and so is a " +
                    "fresh closure allocation every tick too. None of this scales with entity count, which " +
                    "matches a near-constant bytes/tick figure rather than one that grows with 2,000 entities. " +
                    "Not optimized here; see the Module checklist entry in CLAUDE.md for the kind of cost a " +
                    "real module's per-tick iteration should expect to budget for.");
            }

            Assert.Less(averageMs, AverageTickBudgetMs, $"Average tick time {averageMs:F4} ms exceeded the generous {AverageTickBudgetMs} ms budget.");
        }
    }
}
