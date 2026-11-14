using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Rules
{
    /// <summary>
    /// Prompt 2.8: the Milestone 2 performance check. 2,000 entities, 500
    /// rules (300 on events, 200 on schedules) mixing every core block kind,
    /// about 50 events per tick, run for one in-game day. Explicit so it
    /// doesn't slow down the normal suite; run it by name when the numbers
    /// are needed. Targets: rule work under 1ms average, under 5ms worst -
    /// this test only reports the numbers (via TestContext.WriteLine) and
    /// never fails on missing a target, per the prompt ("don't optimize in
    /// this prompt").
    /// </summary>
    [Explicit("Builds 2,000 entities and 500 rules and runs a full in-game day - run by name for performance numbers, not as part of the normal suite.")]
    public class RulePerformanceBenchmarkTests
    {
        [Test]
        public void RunOneDay_ReportsTimingAndAllocations()
        {
            using var source = new TempContentSource("core");
            RuleBenchmarkFixture.WriteContent(source);
            string[] sources = { source.FolderPath };

            var buildStopwatch = Stopwatch.StartNew();
            (Simulation sim, StartupReport startupReport) = SimulationBuilder.Build(1, sources, Array.Empty<IModule>(), ErrorMode.Development);
            buildStopwatch.Stop();

            Assert.IsFalse(startupReport.RuleErrors.Count > 0, string.Join("\n", startupReport.RuleErrors));
            Assert.AreEqual(RuleBenchmarkFixture.RuleCount, sim.Rules.All.Count());

            // Compile time alone, isolated from content loading - the content database is already in memory.
            var compileStopwatch = Stopwatch.StartNew();
            RuleCompiler.Compile(sim.Content, sim.Blocks, sim.Settings, sim.EventDefinitions, ErrorMode.Development);
            compileStopwatch.Stop();

            for (int i = 0; i < RuleBenchmarkFixture.EntityCount; i++)
            {
                sim.Entities.Create(EntityKind.Person);
            }

            // Prompt 2.8b: exclude one in-game hour of warm-up (JIT, empty-collection growth,
            // the rules random stream's lazy first resolution) from the measured sample, so
            // the worst-case number reflects steady-state rule work, not startup noise.
            const long warmUpTicks = CalendarConstants.TicksPerHour;
            long measuredTicks = CalendarConstants.TicksPerDay;
            long totalTicks = warmUpTicks + measuredTicks;

            var totalMs = new List<double>(capacity: (int)measuredTicks);
            var ruleMs = new List<double>(capacity: (int)measuredTicks);
            var tickRecords = new List<TickRecord>(capacity: (int)measuredTicks);

            long allocBefore = TryGetAllocatedBytes();
            var tickStopwatch = new Stopwatch();
            var ruleStopwatch = new Stopwatch();

            for (long tick = 0; tick < totalTicks; tick++)
            {
                tickStopwatch.Restart();

                TickBoundaries boundaries = sim.Clock.Tick();
                long currentTick = sim.Clock.CurrentTick;

                ruleStopwatch.Restart();
                sim.RuleRunner.ProcessQuestionAnswers(currentTick);
                sim.RuleRunner.ResumeDueInstances(currentTick);

                if (boundaries.NewHour) sim.Events.Post(new NewHourEvent(currentTick), currentTick);
                if (boundaries.NewDay) sim.Events.Post(new NewDayEvent(currentTick), currentTick);
                if (boundaries.NewSeason) sim.Events.Post(new NewSeasonEvent(currentTick), currentTick);
                if (boundaries.NewYear) sim.Events.Post(new NewYearEvent(currentTick), currentTick);

                RuleBenchmarkFixture.PostTickEvents(sim, currentTick);

                sim.Scheduler.RunTick(currentTick);
                sim.Events.DeliverPending();
                ruleStopwatch.Stop();

                tickStopwatch.Stop();

                if (tick >= warmUpTicks)
                {
                    totalMs.Add(tickStopwatch.Elapsed.TotalMilliseconds);
                    ruleMs.Add(ruleStopwatch.Elapsed.TotalMilliseconds);
                    tickRecords.Add(new TickRecord(currentTick, ruleStopwatch.Elapsed.TotalMilliseconds, boundaries));
                }
            }

            long allocAfter = TryGetAllocatedBytes();

            (double avg, double worst) = AverageAndWorst(totalMs);
            (double ruleAvg, double ruleWorst) = AverageAndWorst(ruleMs);

            TestContext.WriteLine($"Startup (content load + compile + activate, 500 rules): {buildStopwatch.Elapsed.TotalMilliseconds:F2} ms");
            TestContext.WriteLine($"Compiling 500 rules alone (content already loaded): {compileStopwatch.Elapsed.TotalMilliseconds:F2} ms");
            TestContext.WriteLine($"Warm-up ticks run (excluded from the measurement): {warmUpTicks}");
            TestContext.WriteLine($"Measured ticks run: {measuredTicks}");
            TestContext.WriteLine($"Whole simulation: avg {avg:F4} ms/tick, worst {worst:F4} ms/tick");
            TestContext.WriteLine($"Rule work alone: avg {ruleAvg:F4} ms/tick, worst {ruleWorst:F4} ms/tick");
            TestContext.WriteLine(allocBefore >= 0 && allocAfter >= 0
                ? $"Allocations (tracing off): {(allocAfter - allocBefore) / (double)measuredTicks:F1} bytes/tick average over the measured run"
                : "Allocations: GC.GetAllocatedBytesForCurrentThread unavailable in this runtime; not measured.");

            TestContext.WriteLine("Five slowest measured ticks (rule work):");
            foreach (TickRecord record in tickRecords.OrderByDescending(r => r.RuleMs).Take(5))
            {
                TestContext.WriteLine($"  tick {record.Tick}: {record.RuleMs:F4} ms - {record.Describe()}");
            }

            const double ruleAvgTargetMs = 1.0;
            const double ruleWorstTargetMs = 5.0;
            if (ruleAvg > ruleAvgTargetMs || ruleWorst > ruleWorstTargetMs)
            {
                TestContext.WriteLine($"TARGET MISSED: average target {ruleAvgTargetMs} ms ({(ruleAvg > ruleAvgTargetMs ? "missed" : "met")}), worst target {ruleWorstTargetMs} ms ({(ruleWorst > ruleWorstTargetMs ? "missed" : "met")}).");
            }
            else
            {
                TestContext.WriteLine("Both rule-work targets met.");
            }

            // This test reports; it does not gate on the performance targets (the prompt says not to optimize here).
            Assert.Pass("See TestContext output for timing and allocation numbers.");
        }

        private readonly struct TickRecord
        {
            public long Tick { get; }
            public double RuleMs { get; }
            private readonly TickBoundaries _boundaries;

            public TickRecord(long tick, double ruleMs, TickBoundaries boundaries)
            {
                Tick = tick;
                RuleMs = ruleMs;
                _boundaries = boundaries;
            }

            public string Describe()
            {
                var flags = new List<string>();
                if (_boundaries.NewHour) flags.Add("NewHour");
                if (_boundaries.NewDay) flags.Add("NewDay");
                if (_boundaries.NewSeason) flags.Add("NewSeason");
                if (_boundaries.NewYear) flags.Add("NewYear");
                return flags.Count > 0 ? string.Join("+", flags) : "no calendar boundary";
            }
        }

        private static (double Average, double Worst) AverageAndWorst(List<double> samples)
        {
            double sum = 0;
            double worst = 0;
            foreach (double sample in samples)
            {
                sum += sample;
                if (sample > worst)
                {
                    worst = sample;
                }
            }

            return (sum / samples.Count, worst);
        }

        private static long TryGetAllocatedBytes()
        {
            try
            {
                return GC.GetAllocatedBytesForCurrentThread();
            }
            catch
            {
                return -1;
            }
        }
    }
}
