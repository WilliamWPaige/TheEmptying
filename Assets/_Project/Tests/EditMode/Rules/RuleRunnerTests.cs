using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Rules
{
    public class RuleRunnerTests
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
            IEnumerable<SettingDefinition> extraSettings = null,
            ulong seed = 1,
            params RuleDefinition[] rules)
        {
            var simulation = new Simulation(seed, null, errorMode);
            CoreBlocks.RegisterAll(simulation.Blocks);

            bool declaredMaxFires = false;
            foreach (SettingDefinition setting in extraSettings ?? Enumerable.Empty<SettingDefinition>())
            {
                simulation.Settings.Declare(setting);
                if (setting.Id == RuleRunner.MaxFiresPerTickSettingId)
                {
                    declaredMaxFires = true;
                }
            }

            if (!declaredMaxFires)
            {
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
            }

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

        /// <summary>Wires trigger -> valueBlock -> Write Log Line("{0}") and asserts the logged text after one fire.</summary>
        private static void AssertValueResult(string valueBlockId, Dictionary<string, RuleFieldValueSource> fields, string expected, IEnumerable<SettingDefinition> extraSettings = null)
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.value_test", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("value", valueBlockId, fields),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("value", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(
                logSink: sink,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                extraSettings: extraSettings,
                rules: rule);

            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, sink.Lines.Count);
            Assert.AreEqual(expected, sink.Lines[0]);
        }

        // --- Triggers ---

        [Test]
        public void OnEventTrigger_FiresWhenEventIsDelivered()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.on_event", new List<RuleNode>
            {
                Node("n1", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("test.go") }),
                Node("n2", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("fired") })
            }, new List<RuleConnection> { Conn("n1", "then", "n2", "exec") });

            (Simulation sim, _, RuleCompileReport report) = Build(
                logSink: sink,
                extraEvents: new[] { new EventDefinition("test.go", EventVisibility.Public, 0, 0, "x") },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("test.go", sim.Clock.CurrentTick);
            sim.Step();

            CollectionAssert.Contains(sink.Lines, "fired");
        }

        [Test]
        public void OnEventTrigger_PostedEvent_DeliveredNextTick_WithChainDepthOneHigher()
        {
            var rule = Rule("rule.chain", new List<RuleNode>
            {
                Node("n1", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("test.go") }),
                Node("n2", "block.action.post_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("test.posted") })
            }, new List<RuleConnection> { Conn("n1", "then", "n2", "exec") });

            (Simulation sim, _, RuleCompileReport report) = Build(
                extraEvents: new[]
                {
                    new EventDefinition("test.go", EventVisibility.Public, 0, 0, "x"),
                    new EventDefinition("test.posted", EventVisibility.Public, 0, 0, "x")
                },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            var received = new List<(long Tick, int ChainDepth)>();
            sim.Events.Subscribe("test.posted", posted => received.Add((posted.Tick, posted.ChainDepth)));

            sim.Events.Post("test.go", sim.Clock.CurrentTick, chainDepth: 0);
            sim.Step();

            Assert.AreEqual(0, received.Count, "the posted event should not deliver in the same tick it was posted");

            sim.Step();

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(1, received[0].ChainDepth);
        }

        [Test]
        public void EveryNHours_FiresAtTheCorrectTicksOverTwoDays()
        {
            var sink = new TickRecordingLogSink();
            var rule = Rule("rule.every_hours", new List<RuleNode>
            {
                Node("n1", "block.trigger.every_n_hours", new Dictionary<string, RuleFieldValueSource> { ["hours"] = Lit(5.0) }),
                Node("n2", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("tick") })
            }, new List<RuleConnection> { Conn("n1", "then", "n2", "exec") });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, rules: rule);
            Assert.IsFalse(report.HasErrors);
            sink.CurrentTick = () => sim.Clock.CurrentTick;

            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
            {
                sim.Step();
            }

            long interval = 5 * CalendarConstants.TicksPerHour;
            var expected = new List<long>();
            for (long t = interval; t <= CalendarConstants.TicksPerDay * 2; t += interval)
            {
                expected.Add(t);
            }

            CollectionAssert.AreEqual(expected, sink.Entries.Select(e => e.Tick).ToList());
        }

        [Test]
        public void AtHourOfDay_FiresAtTheCorrectTicksOverTwoDays()
        {
            var sink = new TickRecordingLogSink();
            var rule = Rule("rule.at_hour", new List<RuleNode>
            {
                Node("n1", "block.trigger.at_hour_of_day", new Dictionary<string, RuleFieldValueSource> { ["hour"] = Lit(6.0) }),
                Node("n2", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("hour6") })
            }, new List<RuleConnection> { Conn("n1", "then", "n2", "exec") });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, rules: rule);
            Assert.IsFalse(report.HasErrors);
            sink.CurrentTick = () => sim.Clock.CurrentTick;

            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
            {
                sim.Step();
            }

            long hour6Ticks = 6 * CalendarConstants.TicksPerHour;
            CollectionAssert.AreEqual(new[] { hour6Ticks, hour6Ticks + CalendarConstants.TicksPerDay }, sink.Entries.Select(e => e.Tick).ToList());
        }

        [Test]
        public void OnNewDay_FiresAtTheStartOfEachDay()
        {
            var sink = new TickRecordingLogSink();
            var rule = Rule("rule.new_day", new List<RuleNode>
            {
                Node("n1", "block.trigger.on_new_day"),
                Node("n2", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("newday") })
            }, new List<RuleConnection> { Conn("n1", "then", "n2", "exec") });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, rules: rule);
            Assert.IsFalse(report.HasErrors);
            sink.CurrentTick = () => sim.Clock.CurrentTick;

            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
            {
                sim.Step();
            }

            CollectionAssert.AreEqual(new[] { CalendarConstants.TicksPerDay, CalendarConstants.TicksPerDay * 2 }, sink.Entries.Select(e => e.Tick).ToList());
        }

        // --- Values ---

        [Test]
        public void CompareNumbers_AllOperators()
        {
            AssertValueResult("block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(3.0), ["operator"] = Lit("=") }, "true");
            AssertValueResult("block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("=") }, "false");
            AssertValueResult("block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("!=") }, "true");
            AssertValueResult("block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("<") }, "true");
            AssertValueResult("block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(4.0), ["b"] = Lit(4.0), ["operator"] = Lit("<=") }, "true");
            AssertValueResult("block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(5.0), ["b"] = Lit(4.0), ["operator"] = Lit(">") }, "true");
            AssertValueResult("block.value.compare_numbers", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(4.0), ["b"] = Lit(4.0), ["operator"] = Lit(">=") }, "true");
        }

        [Test]
        public void CompareText_IsOrdinal()
        {
            AssertValueResult("block.value.compare_text", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit("abc"), ["b"] = Lit("abc"), ["operator"] = Lit("=") }, "true");
            AssertValueResult("block.value.compare_text", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit("ABC"), ["b"] = Lit("abc"), ["operator"] = Lit("=") }, "false");
            AssertValueResult("block.value.compare_text", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit("abc"), ["b"] = Lit("abd"), ["operator"] = Lit("!=") }, "true");
        }

        [Test]
        public void AndOrNot_ProduceExpectedBooleans()
        {
            AssertValueResult("block.value.and", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(true), ["b"] = Lit(true) }, "true");
            AssertValueResult("block.value.and", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(true), ["b"] = Lit(false) }, "false");
            AssertValueResult("block.value.or", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(false), ["b"] = Lit(false) }, "false");
            AssertValueResult("block.value.or", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(true), ["b"] = Lit(false) }, "true");
            AssertValueResult("block.value.not", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(false) }, "true");
        }

        [Test]
        public void Math_AllOperators()
        {
            AssertValueResult("block.value.math", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("add") }, "7");
            AssertValueResult("block.value.math", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("subtract") }, "-1");
            AssertValueResult("block.value.math", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("multiply") }, "12");
            AssertValueResult("block.value.math", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(8.0), ["b"] = Lit(4.0), ["operator"] = Lit("divide") }, "2");
            AssertValueResult("block.value.math", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("min") }, "3");
            AssertValueResult("block.value.math", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(3.0), ["b"] = Lit(4.0), ["operator"] = Lit("max") }, "4");
        }

        [Test]
        public void Math_DivideByZero_GivesZero_AndWritesAWarningToTheLog()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.divide_zero", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("math", "block.value.math", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(5.0), ["b"] = Lit(0.0), ["operator"] = Lit("divide") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("math", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(2, sink.Lines.Count);
            StringAssert.Contains("divide by zero", sink.Lines[0]);
            Assert.AreEqual("0", sink.Lines[1]);
        }

        [Test]
        public void RandomNumber_MatchesTheRulesStreamSequence()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.random_number", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("rand", "block.value.random_number", new Dictionary<string, RuleFieldValueSource> { ["min"] = Lit(10.0), ["max"] = Lit(20.0) }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("rand", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(seed: 55, logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            var independent = new RandomService(55);
            float t = independent.GetStream("rules").NextFloat();
            double expected = 10.0 + (t * 10.0);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(expected.ToString(CultureInfo.InvariantCulture), sink.Lines[0]);
        }

        [Test]
        public void RandomChance_MatchesTheRulesStreamSequence()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.random_chance", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("chance", "block.value.random_chance", new Dictionary<string, RuleFieldValueSource> { ["probability"] = Lit(0.5) }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("chance", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(seed: 55, logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            var independent = new RandomService(55);
            bool expected = independent.GetStream("rules").Chance(0.5f);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(expected ? "true" : "false", sink.Lines[0]);
        }

        [Test]
        public void ReadSetting_ReadsCurrentValue_IncludingAfterItChanges()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.read_setting", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("read", "block.value.read_setting", new Dictionary<string, RuleFieldValueSource> { ["setting"] = Lit("setting.test.rate") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("read", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(
                logSink: sink,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                extraSettings: new[] { new SettingDefinition { Id = "setting.test.rate", ValueType = SettingValueType.Number, Default = 1.5, Minimum = 0, Maximum = 10, Category = "Test", Description = "x" } },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            Assert.AreEqual("1.5", sink.Lines[0]);

            sim.Settings.Set("setting.test.rate", 7.25);
            sink.Lines.Clear();
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            Assert.AreEqual("7.25", sink.Lines[0]);
        }

        [Test]
        public void ReadEventParameter_ReadsFromTriggeringEventData()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.read_param", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("read", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("amount") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("read", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            EventData data = new EventData().SetDouble("amount", 12.5);
            sim.Events.Post("go", sim.Clock.CurrentTick, data);
            sim.Step();

            Assert.AreEqual("12.5", sink.Lines[0]);
        }

        [Test]
        public void ReadEventParameter_MissingParameter_IsARuntimeError()
        {
            var rule = Rule("rule.read_param_missing", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("read", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("amount") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("read", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Development,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);

            RuleException ex = Assert.Throws<RuleException>(() => sim.Step());
            Assert.AreEqual("rule.read_param_missing", ex.RuleId);
            Assert.AreEqual("read", ex.NodeId);
        }

        [Test]
        public void ValueNodeFeedingTwoInputs_ComputesOnlyOncePerStep()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.cache_test", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("rand", "block.value.random_number", new Dictionary<string, RuleFieldValueSource> { ["min"] = Lit(0.0), ["max"] = Lit(1000000.0) }),
                Node("math", "block.value.math", new Dictionary<string, RuleFieldValueSource> { ["operator"] = Lit("add") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("rand", "result", "math", "a"),
                Conn("rand", "result", "math", "b"),
                Conn("math", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(seed: 1, logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            var independent = new RandomService(1);
            float t = independent.GetStream("rules").NextFloat();
            double expectedDraw = 0.0 + (t * 1000000.0);
            double expectedSum = expectedDraw * 2;

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, sink.Lines.Count);
            double actual = double.Parse(sink.Lines[0], CultureInfo.InvariantCulture);
            Assert.AreEqual(expectedSum, actual, 0.0001);
        }

        // --- Memory ---

        [Test]
        public void SetLocalThenGetLocal_RoundTrips()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.locals", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("set", "block.action.set_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("counter"), ["value"] = Lit(42.0) }),
                Node("get", "block.value.get_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("counter") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "set", "exec"),
                Conn("set", "then", "log", "exec"),
                Conn("get", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, sink.Lines.Count);
            Assert.AreEqual("42", sink.Lines[0]);
        }

        [Test]
        public void Locals_DoNotPersistAcrossInstances()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.locals_reset", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("get", "block.value.get_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("counter") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("get", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual("0", sink.Lines[0], "an unset local should read as the type's default");
        }

        // --- Flow ---

        [Test]
        public void Branch_FollowsTrueOrFalseOutputBasedOnCondition()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.branch", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("branch", "block.flow.branch", new Dictionary<string, RuleFieldValueSource> { ["condition"] = Lit(true) }),
                Node("logTrue", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("yes") }),
                Node("logFalse", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("no") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "branch", "exec"),
                Conn("branch", "true", "logTrue", "exec"),
                Conn("branch", "false", "logFalse", "exec")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            CollectionAssert.AreEqual(new[] { "yes" }, sink.Lines);
        }

        [Test]
        public void Sequence_RunsAllWiredOutputsInOrder()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.sequence", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("seq", "block.flow.sequence"),
                Node("log1", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("one") }),
                Node("log2", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("two") }),
                Node("log3", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("three") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "seq", "exec"),
                Conn("seq", "then1", "log1", "exec"),
                Conn("seq", "then2", "log2", "exec"),
                Conn("seq", "then4", "log3", "exec")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            CollectionAssert.AreEqual(new[] { "one", "two", "three" }, sink.Lines);
        }

        [Test]
        public void Stop_EndsExecutionImmediately()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.stop", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("stop", "block.flow.stop"),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("unreachable") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "stop", "exec")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(0, sink.Lines.Count);
        }

        // --- Actions ---

        [Test]
        public void PostEvent_WithNamedParameters_DeliversCorrectTypes()
        {
            var rule = Rule("rule.post_params", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("num", "block.value.math", new Dictionary<string, RuleFieldValueSource> { ["operator"] = Lit("add"), ["a"] = Lit(5.0), ["b"] = Lit(2.0) }),
                Node("flag", "block.value.not", new Dictionary<string, RuleFieldValueSource> { ["a"] = Lit(false) }),
                Node("post", "block.action.post_event", new Dictionary<string, RuleFieldValueSource>
                {
                    ["event"] = Lit("test.posted_with_params"),
                    ["param1Name"] = Lit("amount"),
                    ["param2Name"] = Lit("flag")
                })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "post", "exec"),
                Conn("num", "result", "post", "param1"),
                Conn("flag", "result", "post", "param2")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(
                extraEvents: new[]
                {
                    new EventDefinition("go", EventVisibility.Public, 0, 0, "x"),
                    new EventDefinition("test.posted_with_params", EventVisibility.Public, 0, 0, "x")
                },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            double? amount = null;
            bool? flag = null;
            sim.Events.Subscribe("test.posted_with_params", posted =>
            {
                amount = posted.Data.GetDouble("amount");
                flag = posted.Data.GetBool("flag");
            });

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            sim.Step();

            Assert.AreEqual(7.0, amount);
            Assert.IsTrue(flag);
        }

        [Test]
        public void WriteHeraldLine_LogsAndPostsTheHeraldEvent()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.herald", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("herald", "block.action.write_herald_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("A thing happened.") })
            }, new List<RuleConnection> { Conn("trigger", "then", "herald", "exec") });

            (Simulation sim, _, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            string heraldText = null;
            sim.Events.Subscribe(WriteHeraldLineActionBlockType.EventDefinitionId, posted => heraldText = posted.Data.GetString("text"));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, sink.Lines.Count);
            Assert.AreEqual("A thing happened.", sink.Lines[0]);
            Assert.IsNull(heraldText, "the herald event should not be delivered in the same tick it was posted");

            sim.Step();
            Assert.AreEqual("A thing happened.", heraldText);
        }

        // --- Limits and failures ---

        [Test]
        public void SelfTriggeringChain_StopsAtChainDepthTen_AndReports()
        {
            var rule = Rule("rule.self_loop", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("loop") }),
                Node("post", "block.action.post_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("loop") })
            }, new List<RuleConnection> { Conn("trigger", "then", "post", "exec") });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release,
                extraEvents: new[] { new EventDefinition("loop", EventVisibility.Public, 0, 0, "x") },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("loop", sim.Clock.CurrentTick, chainDepth: 0);

            for (int i = 0; i < 15; i++)
            {
                sim.Step();
            }

            RuleRuntimeReport cutoffReport = runner.Reports.Single(r => r.Message.Contains("chain depth"));
            Assert.AreEqual("rule.self_loop", cutoffReport.RuleId);
        }

        [Test]
        public void PerTickFireCap_SkipsFurtherFiresThisTick_AndReports()
        {
            var rule = Rule("rule.capped", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("fired") })
            }, new List<RuleConnection> { Conn("trigger", "then", "log", "exec") });

            var sink = new TestLogSink();
            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release,
                logSink: sink,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                extraSettings: new[] { new SettingDefinition { Id = RuleRunner.MaxFiresPerTickSettingId, ValueType = SettingValueType.Integer, Default = 2, Minimum = 1, Maximum = 1000, Category = "Rules", Description = "x" } },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            for (int i = 0; i < 5; i++)
            {
                sim.Events.Post("go", sim.Clock.CurrentTick);
            }

            sim.Step();

            Assert.AreEqual(2, sink.Lines.Count, "only the first 2 fires this tick should have run");
            RuleRuntimeReport capReport = runner.Reports.Single(r => r.Message.Contains("per-tick fire cap"));
            Assert.AreEqual("rule.capped", capReport.RuleId);
        }

        [Test]
        public void InstanceStepCap_StopsALongRun_AndReports()
        {
            var rule = Rule("rule.infinite_loop", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("branch", "block.flow.branch", new Dictionary<string, RuleFieldValueSource> { ["condition"] = Lit(true) })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "branch", "exec"),
                Conn("branch", "true", "branch", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            Assert.DoesNotThrow(() => sim.Step());

            RuleRuntimeReport stepReport = runner.Reports.Single(r => r.Message.Contains("steps"));
            Assert.AreEqual("rule.infinite_loop", stepReport.RuleId);
        }

        [Test]
        public void DevelopmentMode_RuntimeError_ThrowsNamingRuleAndNode()
        {
            var rule = Rule("rule.throws", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("bad", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("missing") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("bad", "result", "log", "arg0")
            });

            (Simulation sim, _, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Development,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);

            RuleException ex = Assert.Throws<RuleException>(() => sim.Step());
            Assert.AreEqual("rule.throws", ex.RuleId);
            Assert.AreEqual("bad", ex.NodeId);
        }

        [Test]
        public void ReleaseMode_RuntimeError_SwitchesRuleOff_OtherRulesKeepRunning()
        {
            var badRule = Rule("rule.bad", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("bad", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("missing") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("bad", "result", "log", "arg0")
            });

            var goodRule = Rule("rule.good", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("ok") })
            }, new List<RuleConnection> { Conn("trigger", "then", "log", "exec") });

            var sink = new TestLogSink();
            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release,
                logSink: sink,
                extraEvents: new[] { new EventDefinition("go", EventVisibility.Public, 0, 0, "x") },
                rules: new[] { badRule, goodRule });
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            Assert.DoesNotThrow(() => sim.Step());

            Assert.IsFalse(runner.IsRuleActive("rule.bad"));
            Assert.IsTrue(runner.IsRuleActive("rule.good"));
            CollectionAssert.Contains(sink.Lines, "ok");

            RuleRuntimeReport failureReport = runner.Reports.Single(r => r.RuleId == "rule.bad");
            Assert.AreEqual("bad", failureReport.NodeId);

            sink.Lines.Clear();
            sim.Events.Post("go", sim.Clock.CurrentTick);
            Assert.DoesNotThrow(() => sim.Step());
            CollectionAssert.AreEqual(new[] { "ok" }, sink.Lines);
        }

        // --- Determinism ---

        [Test]
        public void Determinism_SameSeedAndRules_IdenticalEventLogsAndStateHashesOverTwoDays()
        {
            RuleDefinition BuildDeterminismRule()
            {
                return Rule("rule.determinism", new List<RuleNode>
                {
                    Node("trigger", "block.trigger.every_n_hours", new Dictionary<string, RuleFieldValueSource> { ["hours"] = Lit(2.0) }),
                    Node("rand", "block.value.random_number", new Dictionary<string, RuleFieldValueSource> { ["min"] = Lit(0.0), ["max"] = Lit(1000.0) }),
                    Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
                }, new List<RuleConnection>
                {
                    Conn("trigger", "then", "log", "exec"),
                    Conn("rand", "result", "log", "arg0")
                });
            }

            var sinkA = new TestLogSink();
            var sinkB = new TestLogSink();

            (Simulation simA, _, RuleCompileReport reportA) = Build(seed: 777, logSink: sinkA, rules: BuildDeterminismRule());
            (Simulation simB, _, RuleCompileReport reportB) = Build(seed: 777, logSink: sinkB, rules: BuildDeterminismRule());
            Assert.IsFalse(reportA.HasErrors);
            Assert.IsFalse(reportB.HasErrors);

            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
            {
                simA.Step();
                simB.Step();
            }

            Assert.Greater(sinkA.Lines.Count, 0);
            CollectionAssert.AreEqual(sinkA.Lines, sinkB.Lines);
            Assert.AreEqual(simA.ComputeStateHash(), simB.ComputeStateHash());
        }
    }
}
