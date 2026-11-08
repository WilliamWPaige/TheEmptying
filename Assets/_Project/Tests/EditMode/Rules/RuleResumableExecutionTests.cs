using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Rules
{
    /// <summary>Part 1-3 of prompt 2.3: resumable execution, Wait, and saving paused instances.</summary>
    public class RuleResumableExecutionTests
    {
        private sealed class TestLogSink : ILogSink
        {
            public List<string> Lines { get; } = new List<string>();
            public void Log(string message) => Lines.Add(message);
        }

        private sealed class TickRecordingLogSink : ILogSink
        {
            public Func<long> CurrentTick = () => 0;
            public List<(long Tick, string Message)> Entries { get; } = new List<(long, string)>();
            public void Log(string message) => Entries.Add((CurrentTick(), message));
        }

        private static RuleNode Node(string id, string blockType, Dictionary<string, RuleFieldValueSource> fields = null)
        {
            return new RuleNode { Id = id, BlockTypeId = blockType, Fields = fields ?? new Dictionary<string, RuleFieldValueSource>() };
        }

        private static RuleConnection Conn(string fromNode, string fromPort, string toNode, string toPort)
        {
            return new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };
        }

        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));

        private static RuleDefinition Rule(string id, List<RuleNode> nodes, List<RuleConnection> connections)
        {
            return new RuleDefinition { Id = id, Name = id, Description = string.Empty, Enabled = true, Nodes = nodes, Connections = connections };
        }

        private static (Simulation Simulation, RuleRunner Runner, RuleCompileReport CompileReport) Build(
            ErrorMode errorMode = ErrorMode.Development,
            ILogSink logSink = null,
            IEnumerable<EventDefinition> extraEvents = null,
            ulong seed = 1,
            params RuleDefinition[] rules)
        {
            var simulation = new Simulation(seed, null, errorMode);
            CoreBlocks.RegisterAll(simulation.Blocks);

            simulation.Settings.Declare(new SettingDefinition
            {
                Id = RuleRunner.MaxFiresPerTickSettingId,
                ValueType = SettingValueType.Integer,
                Default = 1000,
                Minimum = 1,
                Maximum = 1000000,
                Category = "Rules",
                Description = "x"
            });

            foreach (EventDefinition eventDefinition in extraEvents ?? Enumerable.Empty<EventDefinition>())
            {
                simulation.EventDefinitions.Register(eventDefinition);
            }

            var database = new ContentDatabase();
            foreach (RuleDefinition rule in rules)
            {
                database.Set(BuiltInContentTypes.RuleTypeName, rule, "core", "rules.json");
            }

            (RuleRegistry registry, RuleCompileReport compileReport) = RuleCompiler.Compile(database, simulation.Blocks, simulation.Settings, simulation.EventDefinitions, errorMode);

            var runner = new RuleRunner(registry, simulation.Events, simulation.Scheduler, simulation.Random, simulation.Settings, simulation.EventDefinitions, simulation.Memory, logSink, errorMode, () => simulation.Clock.CurrentTick);
            runner.Activate();
            simulation.RuleRunner = runner;

            return (simulation, runner, compileReport);
        }

        // --- Wait timing ---

        [Test]
        public void Wait_MinutesHoursDays_ResumeOnExactTick()
        {
            AssertWaitResumesAt(5.0, "minutes", 5 * CalendarConstants.TicksPerMinute);
            AssertWaitResumesAt(2.0, "hours", 2 * CalendarConstants.TicksPerHour);
            AssertWaitResumesAt(1.0, "days", CalendarConstants.TicksPerDay);
        }

        private static void AssertWaitResumesAt(double amount, string unit, long expectedDuration)
        {
            var sink = new TickRecordingLogSink();
            var rule = Rule("rule.wait_test", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(amount), ["unit"] = Lit(unit) }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("resumed") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "wait", "exec"),
                Conn("wait", "then", "log", "exec")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));
            sink.CurrentTick = () => sim.Clock.CurrentTick;

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            long expectedResumeTick = sim.Clock.CurrentTick + expectedDuration;

            while (sim.Clock.CurrentTick < expectedResumeTick - 1)
            {
                sim.Step();
            }

            Assert.AreEqual(0, sink.Entries.Count, $"[{unit}] should not resume before it is due");

            sim.Step();

            Assert.AreEqual(1, sink.Entries.Count, $"[{unit}]");
            Assert.AreEqual(expectedResumeTick, sink.Entries[0].Tick, $"[{unit}]");
        }

        [Test]
        public void Wait_WiredAmount_ResumesOnTheWiredDuration()
        {
            var sink = new TickRecordingLogSink();
            var rule = Rule("rule.wait_wired", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("math", "block.value.math", new Dictionary<string, RuleFieldValueSource> { ["operator"] = Lit("add"), ["a"] = Lit(2.0), ["b"] = Lit(1.0) }),
                Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["unit"] = Lit("hours") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("resumed") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "wait", "exec"),
                Conn("math", "result", "wait", "amount"),
                Conn("wait", "then", "log", "exec")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));
            sink.CurrentTick = () => sim.Clock.CurrentTick;

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            long expectedResumeTick = sim.Clock.CurrentTick + (3 * CalendarConstants.TicksPerHour);

            while (sim.Clock.CurrentTick < expectedResumeTick - 1)
            {
                sim.Step();
            }

            Assert.AreEqual(0, sink.Entries.Count, "should not resume before the wired amount (3 hours) is due");

            sim.Step();

            Assert.AreEqual(1, sink.Entries.Count);
            Assert.AreEqual(expectedResumeTick, sink.Entries[0].Tick);
        }

        [Test]
        public void WaitInsideSequenceFirstBranch_LaterOutputsStillRunAfterResume_InOrder()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.seq_wait", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("seq", "block.flow.sequence"),
                Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(1.0), ["unit"] = Lit("hours") }),
                Node("logAfterWait", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("after_wait") }),
                Node("log2", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("two") }),
                Node("log3", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("three") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "seq", "exec"),
                Conn("seq", "then1", "wait", "exec"),
                Conn("wait", "then", "logAfterWait", "exec"),
                Conn("seq", "then2", "log2", "exec"),
                Conn("seq", "then3", "log3", "exec")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            CollectionAssert.IsEmpty(sink.Lines, "nothing should run yet; the first branch is paused mid-way");

            for (int i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            CollectionAssert.AreEqual(new[] { "after_wait", "two", "three" }, sink.Lines);
        }

        [Test]
        public void LocalsSetBeforeWait_AreAvailableAfterResume()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.locals_wait", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("set", "block.action.set_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("x"), ["value"] = Lit(99.0) }),
                Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(1.0), ["unit"] = Lit("hours") }),
                Node("get", "block.value.get_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("x") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "set", "exec"),
                Conn("set", "then", "wait", "exec"),
                Conn("wait", "then", "log", "exec"),
                Conn("get", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            for (int i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(1, sink.Lines.Count);
            Assert.AreEqual("99", sink.Lines[0]);
        }

        [Test]
        public void SeveralInstancesDueSameTick_ResumeInInstanceIdOrder()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.multi_wait", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(1.0), ["unit"] = Lit("hours") }),
                Node("read", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("n") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "wait", "exec"),
                Conn("wait", "then", "log", "exec"),
                Conn("read", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick, new EventData().SetDouble("n", 3));
            sim.Events.Post("go", sim.Clock.CurrentTick, new EventData().SetDouble("n", 1));
            sim.Events.Post("go", sim.Clock.CurrentTick, new EventData().SetDouble("n", 2));
            sim.Step();

            for (int i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            CollectionAssert.AreEqual(new[] { "3", "1", "2" }, sink.Lines);
        }

        [Test]
        public void InstanceWhoseRuleIsSwitchedOff_IsDroppedAtResume_AndReported()
        {
            var sink = new TestLogSink();

            // "check" forces "read" to evaluate before the wait; a missing "marker" parameter throws and switches the whole rule off.
            var rule = Rule("rule.drop_test", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("read", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("marker") }),
                Node("check", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("checked") }),
                Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(1.0), ["unit"] = Lit("hours") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("done") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "check", "exec"),
                Conn("read", "result", "check", "arg0"),
                Conn("check", "then", "wait", "exec"),
                Conn("wait", "then", "log", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release,
                logSink: sink,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick, new EventData().SetDouble("marker", 1));
            sim.Step();
            Assert.IsTrue(runner.IsRuleActive("rule.drop_test"));
            CollectionAssert.AreEqual(new[] { "checked" }, sink.Lines);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            Assert.IsFalse(runner.IsRuleActive("rule.drop_test"), "a missing event parameter should fail and switch the rule off");

            for (int i = 0; i < CalendarConstants.TicksPerHour; i++)
            {
                sim.Step();
            }

            CollectionAssert.AreEqual(new[] { "checked" }, sink.Lines, "the dropped instance should never resume and reach 'done'");
            Assert.IsTrue(runner.Reports.Any(r => r.RuleId == "rule.drop_test" && r.Message.Contains("switched off")));
        }

        // --- Saving paused instances ---

        private static TempContentSource WriteKeyTestSource(string waitNodeTargetId = "finish")
        {
            var source = new TempContentSource("core");
            source.WriteDataFile("rules.json", $@"{{
                ""type"": ""rule"",
                ""items"": [
                    {{
                        ""id"": ""rule.key_test"",
                        ""enabled"": true,
                        ""nodes"": [
                            {{ ""id"": ""trigger"", ""blockType"": ""block.trigger.on_event"", ""fields"": {{ ""event"": ""test.start"" }} }},
                            {{ ""id"": ""wait"", ""blockType"": ""block.flow.wait"", ""fields"": {{ ""amount"": 6, ""unit"": ""hours"" }} }},
                            {{ ""id"": ""change"", ""blockType"": ""block.action.change_counter"", ""fields"": {{ ""name"": ""test_counter"", ""amount"": 1 }} }},
                            {{ ""id"": ""{waitNodeTargetId}"", ""blockType"": ""block.action.post_event"", ""fields"": {{ ""event"": ""test.finished"" }} }}
                        ],
                        ""connections"": [
                            {{ ""fromNode"": ""trigger"", ""fromPort"": ""then"", ""toNode"": ""wait"", ""toPort"": ""exec"" }},
                            {{ ""fromNode"": ""wait"", ""fromPort"": ""then"", ""toNode"": ""change"", ""toPort"": ""exec"" }},
                            {{ ""fromNode"": ""change"", ""fromPort"": ""then"", ""toNode"": ""{waitNodeTargetId}"", ""toPort"": ""exec"" }}
                        ]
                    }}
                ]
            }}");

            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""id"": ""test.start"", ""visibility"": ""Public"", ""reportingScore"": 0, ""notabilityScore"": 0, ""description"": ""x"" },
                    { ""id"": ""test.finished"", ""visibility"": ""Public"", ""reportingScore"": 0, ""notabilityScore"": 0, ""description"": ""x"" }
                ]
            }");

            source.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""setting.rules.max_fires_per_tick"", ""valueType"": ""Integer"", ""default"": 1000, ""minimum"": 1, ""maximum"": 1000000, ""category"": ""Rules"", ""description"": ""x"" }
                ]
            }");

            return source;
        }

        [Test]
        public void KeyTest_WaitSixHours_ThenChangeCounterAndPostEvent_SaveLoadMidway_MatchesUnbrokenRun()
        {
            using TempContentSource unbrokenSource = WriteKeyTestSource();
            string[] unbrokenSources = { unbrokenSource.FolderPath };

            (Simulation unbroken, StartupReport unbrokenStartup) = SimulationBuilder.Build(42, unbrokenSources, null);
            Assert.AreEqual(0, unbrokenStartup.RuleErrors.Count, string.Join("\n", unbrokenStartup.RuleErrors));

            var unbrokenFinished = new List<(long Tick, int ChainDepth)>();
            unbroken.Events.Subscribe("test.finished", posted => unbrokenFinished.Add((posted.Tick, posted.ChainDepth)));

            unbroken.Events.Post("test.start", unbroken.Clock.CurrentTick);
            for (int i = 0; i < 12 * CalendarConstants.TicksPerHour; i++)
            {
                unbroken.Step();
            }

            ulong unbrokenHash = unbroken.ComputeStateHash();
            double unbrokenCounter = unbroken.Memory.GetCounter(EntityId.None, "test_counter");

            using TempContentSource interruptedSource = WriteKeyTestSource();
            string[] interruptedSources = { interruptedSource.FolderPath };

            (Simulation day1, StartupReport day1Startup) = SimulationBuilder.Build(42, interruptedSources, null);
            Assert.AreEqual(0, day1Startup.RuleErrors.Count, string.Join("\n", day1Startup.RuleErrors));

            var interruptedFinished = new List<(long Tick, int ChainDepth)>();
            day1.Events.Subscribe("test.finished", posted => interruptedFinished.Add((posted.Tick, posted.ChainDepth)));

            day1.Events.Post("test.start", day1.Clock.CurrentTick);
            for (int i = 0; i < 3 * CalendarConstants.TicksPerHour; i++)
            {
                day1.Step();
            }

            using var stream = new MemoryStream();
            GameSave.Save(day1, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            (Simulation day2, _) = GameSave.Load(stream, interruptedSources, null);
            day2.Events.Subscribe("test.finished", posted => interruptedFinished.Add((posted.Tick, posted.ChainDepth)));

            for (int i = 0; i < 9 * CalendarConstants.TicksPerHour; i++)
            {
                day2.Step();
            }

            ulong interruptedHash = day2.ComputeStateHash();
            double interruptedCounter = day2.Memory.GetCounter(EntityId.None, "test_counter");

            Assert.AreEqual(1.0, unbrokenCounter, "sanity: the counter should have changed exactly once over the unbroken run");
            Assert.AreEqual(unbrokenCounter, interruptedCounter);
            CollectionAssert.AreEqual(unbrokenFinished, interruptedFinished);
            Assert.AreEqual(unbrokenHash, interruptedHash, "state hash after an interrupted run should match an unbroken run from the same seed.");
        }

        [Test]
        public void SavedInstanceWhoseRuleNoLongerExists_IsDroppedOnLoad_AndReported()
        {
            using var originalSource = WriteKeyTestSource();
            string[] originalSources = { originalSource.FolderPath };

            (Simulation original, StartupReport originalStartup) = SimulationBuilder.Build(1, originalSources, null);
            Assert.AreEqual(0, originalStartup.RuleErrors.Count, string.Join("\n", originalStartup.RuleErrors));

            original.Events.Post("test.start", original.Clock.CurrentTick);
            original.Step();

            using var stream = new MemoryStream();
            GameSave.Save(original, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            // Load with a source that no longer declares the rule at all.
            using var emptySource = new TempContentSource("core");
            emptySource.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""setting.rules.max_fires_per_tick"", ""valueType"": ""Integer"", ""default"": 1000, ""minimum"": 1, ""maximum"": 1000000, ""category"": ""Rules"", ""description"": ""x"" }
                ]
            }");

            (Simulation loaded, _) = GameSave.Load(stream, new[] { emptySource.FolderPath }, null);

            Assert.IsTrue(loaded.RuleRunner.Reports.Any(r => r.Message.Contains("no longer exists")));
        }

        [Test]
        public void SavedInstanceWhoseNodeNoLongerExists_IsDroppedOnLoad_AndReported()
        {
            using var originalSource = WriteKeyTestSource(waitNodeTargetId: "finish");
            string[] originalSources = { originalSource.FolderPath };

            (Simulation original, StartupReport originalStartup) = SimulationBuilder.Build(1, originalSources, null);
            Assert.AreEqual(0, originalStartup.RuleErrors.Count, string.Join("\n", originalStartup.RuleErrors));

            original.Events.Post("test.start", original.Clock.CurrentTick);
            original.Step();

            using var stream = new MemoryStream();
            GameSave.Save(original, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            // Same rule Id, but the node the paused instance was due to resume at ("change") now has a different Id.
            using var changedSource = new TempContentSource("core");
            changedSource.WriteDataFile("rules.json", @"{
                ""type"": ""rule"",
                ""items"": [
                    {
                        ""id"": ""rule.key_test"",
                        ""enabled"": true,
                        ""nodes"": [
                            { ""id"": ""trigger"", ""blockType"": ""block.trigger.on_event"", ""fields"": { ""event"": ""test.start"" } },
                            { ""id"": ""wait"", ""blockType"": ""block.flow.wait"", ""fields"": { ""amount"": 6, ""unit"": ""hours"" } },
                            { ""id"": ""change_renamed"", ""blockType"": ""block.action.change_counter"", ""fields"": { ""name"": ""test_counter"", ""amount"": 1 } },
                            { ""id"": ""finish"", ""blockType"": ""block.action.post_event"", ""fields"": { ""event"": ""test.finished"" } }
                        ],
                        ""connections"": [
                            { ""fromNode"": ""trigger"", ""fromPort"": ""then"", ""toNode"": ""wait"", ""toPort"": ""exec"" },
                            { ""fromNode"": ""wait"", ""fromPort"": ""then"", ""toNode"": ""change_renamed"", ""toPort"": ""exec"" },
                            { ""fromNode"": ""change_renamed"", ""fromPort"": ""then"", ""toNode"": ""finish"", ""toPort"": ""exec"" }
                        ]
                    }
                ]
            }");
            changedSource.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""id"": ""test.start"", ""visibility"": ""Public"", ""reportingScore"": 0, ""notabilityScore"": 0, ""description"": ""x"" },
                    { ""id"": ""test.finished"", ""visibility"": ""Public"", ""reportingScore"": 0, ""notabilityScore"": 0, ""description"": ""x"" }
                ]
            }");
            changedSource.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""setting.rules.max_fires_per_tick"", ""valueType"": ""Integer"", ""default"": 1000, ""minimum"": 1, ""maximum"": 1000000, ""category"": ""Rules"", ""description"": ""x"" }
                ]
            }");

            (Simulation loaded, _) = GameSave.Load(stream, new[] { changedSource.FolderPath }, null);

            Assert.IsTrue(loaded.RuleRunner.Reports.Any(r => r.Message.Contains("changed shape")));
        }
    }
}
