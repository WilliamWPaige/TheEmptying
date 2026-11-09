using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
using TheEmptying.Core.Rules.Tracing;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Rules
{
    /// <summary>Prompt 2.7: the trace listener - contents, zero cost with no listener, and determinism with/without one.</summary>
    public class RuleTracingTests
    {
        private sealed class TestLogSink : ILogSink
        {
            public List<string> Lines { get; } = new List<string>();
            public void Log(string message) => Lines.Add(message);
        }

        private sealed class RecordingTraceListener : IRuleTraceListener
        {
            public List<RuleTraceSegment> Segments { get; } = new List<RuleTraceSegment>();
            public void OnSegmentCompleted(RuleTraceSegment segment) => Segments.Add(segment);
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

        private static readonly EventDefinition GoEvent = new EventDefinition("go", EventVisibility.Public, 0, 0, "x");

        // --- Zero cost with no listener ---

        [Test]
        public void WithNoListenerAttached_NoSegmentIsEverProduced()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.untraced", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("hi") })
            }, new List<RuleConnection> { Conn("trigger", "then", "log", "exec") });

            var otherRule = Rule("rule.other", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("other_event") })
            }, new List<RuleConnection>());

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                extraEvents: new[] { GoEvent, new EventDefinition("other_event", EventVisibility.Public, 0, 0, "x") },
                rules: new[] { rule, otherRule });
            Assert.IsFalse(report.HasErrors);

            // A listener attached to a DIFFERENT, never-fired rule must not affect this one at all.
            runner.SetTraceListener("rule.other", listener);

            Assert.IsFalse(runner.IsTracingEnabled("rule.untraced"));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.IsEmpty(listener.Segments, "no segment should ever be produced for a rule with no listener attached.");
        }

        [Test]
        public void SetAndClearTraceListener_TurnsRecordingOnAndOff()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.toggle", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("hi") })
            }, new List<RuleConnection> { Conn("trigger", "then", "log", "exec") });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            Assert.IsEmpty(listener.Segments, "not attached yet.");

            runner.SetTraceListener("rule.toggle", listener);
            Assert.IsTrue(runner.IsTracingEnabled("rule.toggle"));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            Assert.AreEqual(1, listener.Segments.Count);

            runner.ClearTraceListener("rule.toggle");
            Assert.IsFalse(runner.IsTracingEnabled("rule.toggle"));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            Assert.AreEqual(1, listener.Segments.Count, "no new segment after the listener was cleared.");
        }

        // --- Trace contents: every core block kind, in order ---

        [Test]
        public void TraceCoversTriggerValueMemoryAndAction_InOrder_WithCorrectValues()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.full_coverage", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("set", "block.action.set_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("n"), ["value"] = Lit(5.0) }),
                Node("get", "block.value.get_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("n") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "set", "exec"),
                Conn("set", "then", "log", "exec"),
                Conn("get", "result", "log", "arg0")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.full_coverage", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, listener.Segments.Count);
            RuleTraceSegment segment = listener.Segments[0];

            Assert.AreEqual("rule.full_coverage", segment.RuleId);
            Assert.AreEqual(RuleTraceStartKind.EventTrigger, segment.StartKind);
            Assert.AreEqual("go", segment.StartEventDefinitionId);
            Assert.AreEqual(RuleTraceEndKind.Ended, segment.EndKind);

            List<RuleTraceEntry> entries = segment.Entries.ToList();

            // trigger -> set (NodeExecuted) -> set reads its field (InputRead) -> local set (LocalSet)
            //   -> set -> log (NodeExecuted) -> get computes its value (ValueComputed) -> log reads arg0 (InputRead) -> log logs (LineLogged)
            Assert.IsTrue(entries.Any(e => e.Kind == RuleTraceEntryKind.NodeExecuted && e.NodeId == "set" && e.PortOrFieldName == "then"));
            Assert.IsTrue(entries.Any(e => e.Kind == RuleTraceEntryKind.LocalSet && e.NodeId == "set" && e.PortOrFieldName == "n" && e.Value.Value.AsNumber == 5.0));
            Assert.IsTrue(entries.Any(e => e.Kind == RuleTraceEntryKind.ValueComputed && e.NodeId == "get" && e.Value.Value.AsNumber == 5.0));
            Assert.IsTrue(entries.Any(e => e.Kind == RuleTraceEntryKind.InputRead && e.NodeId == "log" && e.PortOrFieldName == "arg0" && e.Value.Value.AsNumber == 5.0));
            Assert.IsTrue(entries.Any(e => e.Kind == RuleTraceEntryKind.LineLogged && e.NodeId == "log" && e.Message == "5"));

            // A node's own NodeExecuted entry (which output it followed) is only known once
            // its Execute call returns, so it comes AFTER that same call's own effects
            // (its field/wire reads, any local it sets) - but still before the next node's.
            int setExecutedIndex = entries.FindIndex(e => e.Kind == RuleTraceEntryKind.NodeExecuted && e.NodeId == "set");
            int localSetIndex = entries.FindIndex(e => e.Kind == RuleTraceEntryKind.LocalSet);
            int loggedIndex = entries.FindIndex(e => e.Kind == RuleTraceEntryKind.LineLogged);
            Assert.Less(localSetIndex, setExecutedIndex, "the local must be set during 'set's own Execute call, before its NodeExecuted entry (which needs the call's return value).");
            Assert.Less(setExecutedIndex, loggedIndex, "'set' must finish entirely before 'log' (the next node) starts.");
            Assert.Less(localSetIndex, loggedIndex, "the local must be set before the log line that depends on it is written.");
        }

        [Test]
        public void TraceCoversBranchFlow()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.branch_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("branch", "block.flow.branch", new Dictionary<string, RuleFieldValueSource> { ["condition"] = Lit(true) }),
                Node("logTrue", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("yes") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "branch", "exec"),
                Conn("branch", "true", "logTrue", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.branch_trace", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            RuleTraceSegment segment = listener.Segments.Single();
            Assert.IsTrue(segment.Entries.Any(e => e.Kind == RuleTraceEntryKind.NodeExecuted && e.NodeId == "branch" && e.PortOrFieldName == "true"));
        }

        [Test]
        public void TraceCoversSequence_InWiredOrder()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.sequence_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("seq", "block.flow.sequence"),
                Node("log1", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("one") }),
                Node("log2", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("two") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "seq", "exec"),
                Conn("seq", "then1", "log1", "exec"),
                Conn("seq", "then2", "log2", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.sequence_trace", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            List<string> loggedNodeOrder = listener.Segments.Single().Entries
                .Where(e => e.Kind == RuleTraceEntryKind.LineLogged)
                .Select(e => e.Message)
                .ToList();

            CollectionAssert.AreEqual(new[] { "one", "two" }, loggedNodeOrder);
        }

        // --- Wait and resume ---

        [Test]
        public void WaitThenResume_ProducesAWaitingSegmentThenAResumeSegment()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.wait_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(1.0), ["unit"] = Lit("hours") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("resumed") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "wait", "exec"),
                Conn("wait", "then", "log", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.wait_trace", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, listener.Segments.Count);
            Assert.AreEqual(RuleTraceEndKind.Waiting, listener.Segments[0].EndKind);
            long resumeTick = listener.Segments[0].EndResumeTick;
            Assert.Greater(resumeTick, sim.Clock.CurrentTick);

            for (long i = sim.Clock.CurrentTick; i < resumeTick; i++)
            {
                sim.Step();
            }

            Assert.AreEqual(2, listener.Segments.Count);
            RuleTraceSegment resumeSegment = listener.Segments[1];
            Assert.AreEqual(RuleTraceStartKind.Resume, resumeSegment.StartKind);
            Assert.AreEqual(resumeTick, resumeSegment.StartResumeTick);
            Assert.AreEqual(RuleTraceEndKind.Ended, resumeSegment.EndKind);
            Assert.IsTrue(resumeSegment.Entries.Any(e => e.Kind == RuleTraceEntryKind.LineLogged && e.Message == "resumed"));
        }

        // --- Question and answer ---

        private static Dictionary<string, RuleFieldValueSource> AskFields(string choice1 = "A", string choice2 = "B") =>
            new Dictionary<string, RuleFieldValueSource> { ["choice1"] = Lit(choice1), ["choice2"] = Lit(choice2) };

        [Test]
        public void AskThenAnswer_ProducesAnAskingSegmentThenAnAnswerSegment()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.ask_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields()),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("chose1") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "ask", "exec"),
                Conn("ask", "choice1", "log", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.ask_trace", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, listener.Segments.Count);
            Assert.AreEqual(RuleTraceEndKind.Asking, listener.Segments[0].EndKind);
            long questionId = listener.Segments[0].EndQuestionId;
            Assert.Greater(questionId, 0);

            Assert.IsNull(runner.QueueAnswer(questionId, 1));
            sim.Step();

            Assert.AreEqual(2, listener.Segments.Count);
            RuleTraceSegment answerSegment = listener.Segments[1];
            Assert.AreEqual(RuleTraceStartKind.Answer, answerSegment.StartKind);
            Assert.AreEqual(questionId, answerSegment.StartQuestionId);
            Assert.AreEqual(1, answerSegment.StartChoiceNumber);
            Assert.IsTrue(answerSegment.Entries.Any(e => e.Kind == RuleTraceEntryKind.LineLogged && e.Message == "chose1"));
        }

        // --- Each end reason ---

        [Test]
        public void ChainDepthCutOff_ProducesAZeroStepSegment()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.chain_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("loop") }),
                Node("post", "block.action.post_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("loop") })
            }, new List<RuleConnection> { Conn("trigger", "then", "post", "exec") });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release,
                extraEvents: new[] { new EventDefinition("loop", EventVisibility.Public, 0, 0, "x") },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.chain_trace", listener);
            sim.Events.Post("loop", sim.Clock.CurrentTick, chainDepth: 0);

            for (int i = 0; i < 15; i++)
            {
                sim.Step();
            }

            Assert.IsTrue(listener.Segments.Any(s => s.EndKind == RuleTraceEndKind.ChainDepthCutOff));
        }

        [Test]
        public void FireCap_ProducesAZeroStepSegment()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.cap_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("fired") })
            }, new List<RuleConnection> { Conn("trigger", "then", "log", "exec") });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release,
                extraEvents: new[] { GoEvent },
                extraSettings: new[] { new SettingDefinition { Id = RuleRunner.MaxFiresPerTickSettingId, ValueType = SettingValueType.Integer, Default = 1, Minimum = 1, Maximum = 1000, Category = "Rules", Description = "x" } },
                rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.cap_trace", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.IsTrue(listener.Segments.Any(s => s.EndKind == RuleTraceEndKind.FireCap));
            Assert.IsTrue(listener.Segments.Any(s => s.EndKind == RuleTraceEndKind.Ended));
        }

        [Test]
        public void InstanceStepCap_ProducesAStepCapSegment()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.stepcap_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("branch", "block.flow.branch", new Dictionary<string, RuleFieldValueSource> { ["condition"] = Lit(true) })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "branch", "exec"),
                Conn("branch", "true", "branch", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(errorMode: ErrorMode.Release, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.stepcap_trace", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.AreEqual(1, listener.Segments.Count);
            Assert.AreEqual(RuleTraceEndKind.StepCap, listener.Segments[0].EndKind);
        }

        [Test]
        public void RuntimeError_ProducesAnErrorSegment_InReleaseMode()
        {
            var listener = new RecordingTraceListener();
            var rule = Rule("rule.error_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("bad", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("missing") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("bad", "result", "log", "arg0")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(errorMode: ErrorMode.Release, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors);

            runner.SetTraceListener("rule.error_trace", listener);
            sim.Events.Post("go", sim.Clock.CurrentTick);
            Assert.DoesNotThrow(() => sim.Step());

            Assert.AreEqual(1, listener.Segments.Count);
            Assert.AreEqual(RuleTraceEndKind.Error, listener.Segments[0].EndKind);
            Assert.IsNotNull(listener.Segments[0].EndErrorMessage);
        }

        // --- Determinism ---

        [Test]
        public void Determinism_WithAndWithoutAListener_IdenticalEventLogsAndStateHashes()
        {
            RuleDefinition BuildDeterminismRule()
            {
                return Rule("rule.determinism_trace", new List<RuleNode>
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

            (Simulation simA, RuleRunner runnerA, RuleCompileReport reportA) = Build(seed: 777, logSink: sinkA, rules: BuildDeterminismRule());
            (Simulation simB, RuleRunner runnerB, RuleCompileReport reportB) = Build(seed: 777, logSink: sinkB, rules: BuildDeterminismRule());
            Assert.IsFalse(reportA.HasErrors);
            Assert.IsFalse(reportB.HasErrors);

            // B has a listener attached for the whole run; A has none.
            runnerB.SetTraceListener("rule.determinism_trace", new RecordingTraceListener());

            for (long i = 0; i < CalendarConstants.TicksPerDay * 2; i++)
            {
                simA.Step();
                simB.Step();
            }

            Assert.Greater(sinkA.Lines.Count, 0);
            CollectionAssert.AreEqual(sinkA.Lines, sinkB.Lines, "tracing must never change what a rule does, including the values its random blocks draw.");
            Assert.AreEqual(simA.ComputeStateHash(), simB.ComputeStateHash());
        }

        [Test]
        public void RandomBlocks_DrawTheSameValues_WithTracingOnOrOff()
        {
            var rule = Rule("rule.random_trace", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("rand", "block.value.random_number", new Dictionary<string, RuleFieldValueSource> { ["min"] = Lit(0.0), ["max"] = Lit(1000.0) }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "log", "exec"),
                Conn("rand", "result", "log", "arg0")
            });

            var sinkOff = new TestLogSink();
            (Simulation simOff, RuleRunner runnerOff, RuleCompileReport reportOff) = Build(seed: 99, logSink: sinkOff, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(reportOff.HasErrors);
            simOff.Events.Post("go", simOff.Clock.CurrentTick);
            simOff.Step();

            var sinkOn = new TestLogSink();
            (Simulation simOn, RuleRunner runnerOn, RuleCompileReport reportOn) = Build(seed: 99, logSink: sinkOn, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(reportOn.HasErrors);
            runnerOn.SetTraceListener("rule.random_trace", new RecordingTraceListener());
            simOn.Events.Post("go", simOn.Clock.CurrentTick);
            simOn.Step();

            Assert.AreEqual(sinkOff.Lines[0], sinkOn.Lines[0], "the random draw's value must not depend on whether a trace listener is attached.");
        }
    }
}
