using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Rules
{
    /// <summary>
    /// Prompt 2.8: save and load under load. Using the same 500-rule,
    /// 2,000-entity benchmark setup (its Wait and Ask Player rules keep
    /// paused instances, pending questions and outstanding timeouts in
    /// flight throughout the day), saves at three different points in the
    /// day, loads each, and runs to the end of the day. State hashes and
    /// log lines must match an unbroken run of the same length.
    /// </summary>
    public class RuleSaveLoadUnderLoadTests
    {
        private sealed class TestLogSink : ILogSink
        {
            public List<string> Lines { get; } = new List<string>();
            public void Log(string message) => Lines.Add(message);
        }

        private static void RunTicks(Simulation simulation, long count)
        {
            for (long i = 0; i < count; i++)
            {
                long nextTick = simulation.Clock.CurrentTick + 1;
                RuleBenchmarkFixture.PostTickEvents(simulation, nextTick);
                simulation.Step();
            }
        }

        private static Simulation BuildAndPopulate(string[] sources, TestLogSink logSink)
        {
            (Simulation simulation, StartupReport report) = SimulationBuilder.Build(1, sources, Array.Empty<IModule>(), ErrorMode.Development, logSink: logSink);
            Assert.IsFalse(report.RuleErrors.Count > 0, string.Join("\n", report.RuleErrors));

            for (int i = 0; i < RuleBenchmarkFixture.EntityCount; i++)
            {
                simulation.Entities.Create(EntityKind.Person);
            }

            return simulation;
        }

        [Test]
        public void SaveAtThreePointsInTheDay_EachLoadsAndMatchesAnUnbrokenRun()
        {
            using var source = new TempContentSource("core");
            RuleBenchmarkFixture.WriteContent(source);
            string[] sources = { source.FolderPath };

            long ticksPerDay = CalendarConstants.TicksPerDay;

            var truthSink = new TestLogSink();
            Simulation truth = BuildAndPopulate(sources, truthSink);
            RunTicks(truth, ticksPerDay);
            ulong truthHash = truth.ComputeStateHash();

            long[] savePoints = { ticksPerDay / 7, ticksPerDay / 2, (ticksPerDay * 5) / 6 };

            foreach (long savePoint in savePoints)
            {
                var sink = new TestLogSink();
                Simulation sim = BuildAndPopulate(sources, sink);
                RunTicks(sim, savePoint);

                bool hasPausedOrPending = sim.RuleRunner.PausedInstances.Any() || sim.RuleRunner.PendingQuestions.Count > 0;
                Assert.IsTrue(hasPausedOrPending, $"save point {savePoint}: expected at least one paused instance or pending question outstanding (the Wait and Ask Player rules should guarantee this).");

                using var stream = new System.IO.MemoryStream();
                GameSave.Save(sim, new SaveHeaderInfo { KingdomName = "bench", Population = RuleBenchmarkFixture.EntityCount }, stream);
                stream.Position = 0;

                (Simulation loaded, LoadReport loadReport) = GameSave.Load(stream, sources, Array.Empty<IModule>(), ErrorMode.Development, sink);
                Assert.AreEqual(savePoint, loaded.Clock.CurrentTick, $"save point {savePoint}: a load must resume at the exact saved tick.");

                RunTicks(loaded, ticksPerDay - savePoint);

                Assert.AreEqual(truthHash, loaded.ComputeStateHash(), $"save point {savePoint}: state hash must match an unbroken {ticksPerDay}-tick run.");
                CollectionAssert.AreEqual(truthSink.Lines, sink.Lines, $"save point {savePoint}: log lines must match an unbroken run exactly, including everything a paused Wait or a timed-out question logs after resuming.");
            }
        }
    }
}
