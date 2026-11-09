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

            long ticks = CalendarConstants.TicksPerDay;
            var totalMs = new List<double>(capacity: (int)ticks);
            var ruleMs = new List<double>(capacity: (int)ticks);

            long allocBefore = TryGetAllocatedBytes();
            var tickStopwatch = new Stopwatch();
            var ruleStopwatch = new Stopwatch();

            for (long tick = 0; tick < ticks; tick++)
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

                totalMs.Add(tickStopwatch.Elapsed.TotalMilliseconds);
                ruleMs.Add(ruleStopwatch.Elapsed.TotalMilliseconds);
            }

            long allocAfter = TryGetAllocatedBytes();

            (double avg, double worst) = AverageAndWorst(totalMs);
            (double ruleAvg, double ruleWorst) = AverageAndWorst(ruleMs);

            TestContext.WriteLine($"Startup (content load + compile + activate, 500 rules): {buildStopwatch.Elapsed.TotalMilliseconds:F2} ms");
            TestContext.WriteLine($"Compiling 500 rules alone (content already loaded): {compileStopwatch.Elapsed.TotalMilliseconds:F2} ms");
            TestContext.WriteLine($"Ticks run: {ticks}");
            TestContext.WriteLine($"Whole simulation: avg {avg:F4} ms/tick, worst {worst:F4} ms/tick");
            TestContext.WriteLine($"Rule work alone: avg {ruleAvg:F4} ms/tick, worst {ruleWorst:F4} ms/tick");
            TestContext.WriteLine(allocBefore >= 0 && allocAfter >= 0
                ? $"Allocations (tracing off): {(allocAfter - allocBefore) / (double)ticks:F1} bytes/tick average over the whole run"
                : "Allocations: GC.GetAllocatedBytesForCurrentThread unavailable in this runtime; not measured.");

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
