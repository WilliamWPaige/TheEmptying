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
    /// <summary>Prompt 2.4: asking the player - questions, pausing, delegation, saving.</summary>
    public class AskPlayerQuestionTests
    {
        private sealed class TestLogSink : ILogSink
        {
            public List<string> Lines { get; } = new List<string>();
            public void Log(string message) => Lines.Add(message);
        }

        private sealed class FirstChoiceDelegate : IQuestionDelegate
        {
            public string Category => "0";
            public bool TryAnswer(QuestionInfo question, out int choiceNumber)
            {
                choiceNumber = question.Choices[0].ChoiceNumber;
                return true;
            }
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

        private static readonly EventDefinition GoEvent = new EventDefinition("go", EventVisibility.Public, 0, 0, "x");

        private static Dictionary<string, RuleFieldValueSource> AskFields(
            string choice1 = "A", string choice2 = "B", string choice3 = "", string choice4 = "",
            double defaultChoice = 1.0, double timeoutHours = 0.0, string title = "Q", string body = "B")
        {
            return new Dictionary<string, RuleFieldValueSource>
            {
                ["title"] = Lit(title),
                ["body"] = Lit(body),
                ["choice1"] = Lit(choice1),
                ["choice2"] = Lit(choice2),
                ["choice3"] = Lit(choice3),
                ["choice4"] = Lit(choice4),
                ["defaultChoice"] = Lit(defaultChoice),
                ["timeoutHours"] = Lit(timeoutHours)
            };
        }

        // --- Compile errors ---

        [Test]
        public void Compile_FewerThanTwoChoicesFilled_IsAnError()
        {
            var rule = Rule("rule.ask_bad1", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields(choice1: "A", choice2: "", choice3: "", choice4: ""))
            }, new List<RuleConnection> { Conn("trigger", "then", "ask", "exec") });

            (_, _, RuleCompileReport report) = Build(errorMode: ErrorMode.Release, extraEvents: new[] { GoEvent }, rules: rule);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Errors.Any(e => e.Message.Contains("at least two choices")), string.Join("\n", report.Errors));
        }

        [Test]
        public void Compile_WiredChoiceOutputWithEmptyText_IsAnError()
        {
            var rule = Rule("rule.ask_bad2", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields(choice1: "A", choice2: "", choice3: "C", choice4: "")),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("x") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "ask", "exec"),
                Conn("ask", "choice2", "log", "exec")
            });

            (_, _, RuleCompileReport report) = Build(errorMode: ErrorMode.Release, extraEvents: new[] { GoEvent }, rules: rule);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Errors.Any(e => e.Message.Contains("choice2") && e.Message.Contains("empty")), string.Join("\n", report.Errors));
        }

        [Test]
        public void Compile_DefaultChoicePointsAtEmptyChoice_IsAnError()
        {
            var rule = Rule("rule.ask_bad3", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields(choice1: "A", choice2: "B", choice3: "", choice4: "", defaultChoice: 3.0))
            }, new List<RuleConnection> { Conn("trigger", "then", "ask", "exec") });

            (_, _, RuleCompileReport report) = Build(errorMode: ErrorMode.Release, extraEvents: new[] { GoEvent }, rules: rule);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Errors.Any(e => e.Message.Contains("default choice")), string.Join("\n", report.Errors));
        }

        // --- Asking, pausing, answering ---

        [Test]
        public void Asking_PausesTheInstance_AndAddsTheQuestion()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.ask_basic", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields(title: "Pick one", body: "Body text")),
                Node("logYes", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("yes_branch") }),
                Node("logNo", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("no_branch") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "ask", "exec"),
                Conn("ask", "choice1", "logYes", "exec"),
                Conn("ask", "choice2", "logNo", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            CollectionAssert.IsEmpty(sink.Lines, "neither branch should have run yet; the instance is paused on the question");
            Assert.AreEqual(1, runner.PendingQuestions.Count);

            QuestionInfo question = runner.PendingQuestions[0];
            Assert.AreEqual("Pick one", question.Title);
            Assert.AreEqual("Body text", question.Body);
            CollectionAssert.AreEqual(new[] { (1, "A"), (2, "B") }, question.Choices);
            Assert.AreEqual(1, question.DefaultChoiceNumber);
        }

        [Test]
        public void Answering_ContinuesAlongTheRightOutput_WithLocalsIntact()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.ask_locals", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("set", "block.action.set_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("x"), ["value"] = Lit(42.0) }),
                Node("ask", "block.flow.ask_player", AskFields()),
                Node("get", "block.value.get_local.number", new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("x") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "set", "exec"),
                Conn("set", "then", "ask", "exec"),
                Conn("ask", "choice2", "log", "exec"),
                Conn("get", "result", "log", "arg0")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            long questionId = runner.PendingQuestions[0].QuestionId;
            Assert.IsNull(runner.QueueAnswer(questionId, 2));

            sim.Step();

            CollectionAssert.AreEqual(new[] { "42" }, sink.Lines);
            Assert.AreEqual(0, runner.PendingQuestions.Count);
        }

        [Test]
        public void AnswersApply_AtStartOfNextTick_InQuestionIdOrder()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.ask_order", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields()),
                Node("read", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("n") }),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "ask", "exec"),
                Conn("ask", "choice1", "log", "exec"),
                Conn("read", "result", "log", "arg0")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick, new EventData().SetDouble("n", 1));
            sim.Events.Post("go", sim.Clock.CurrentTick, new EventData().SetDouble("n", 2));
            sim.Step();

            Assert.AreEqual(2, runner.PendingQuestions.Count);
            long firstQuestionId = runner.PendingQuestions[0].QuestionId;
            long secondQuestionId = runner.PendingQuestions[1].QuestionId;

            // Queue in reverse order on purpose: processing must still follow question Id order.
            Assert.IsNull(runner.QueueAnswer(secondQuestionId, 1));
            Assert.IsNull(runner.QueueAnswer(firstQuestionId, 1));

            sim.Step();

            CollectionAssert.AreEqual(new[] { "1", "2" }, sink.Lines);
        }

        // --- Timeout ---

        [Test]
        public void Timeout_PicksTheDefault_AfterTheLimit_AndNotBefore()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.ask_timeout", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields(defaultChoice: 2.0, timeoutHours: 2.0)),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("timeout_branch") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "ask", "exec"),
                Conn("ask", "choice2", "log", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            long expectedTimeoutTick = sim.Clock.CurrentTick + (2 * CalendarConstants.TicksPerHour);

            while (sim.Clock.CurrentTick < expectedTimeoutTick - 1)
            {
                sim.Step();
            }

            Assert.AreEqual(0, sink.Lines.Count, "should not resolve before the timeout");
            Assert.AreEqual(1, runner.PendingQuestions.Count);

            sim.Step();

            CollectionAssert.AreEqual(new[] { "timeout_branch" }, sink.Lines);
            Assert.AreEqual(0, runner.PendingQuestions.Count);
        }

        // --- Delegation ---

        [Test]
        public void Delegate_AnswersBeforeTheQueue()
        {
            var sink = new TestLogSink();
            var rule = Rule("rule.ask_delegate", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields()),
                Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("delegate_branch") })
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "ask", "exec"),
                Conn("ask", "choice1", "log", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(logSink: sink, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            runner.RegisterQuestionDelegate(new FirstChoiceDelegate());

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            CollectionAssert.AreEqual(new[] { "delegate_branch" }, sink.Lines);
            Assert.AreEqual(0, runner.PendingQuestions.Count, "the delegate should have answered before the question ever reached the queue");
        }

        // --- Rejected answers ---

        [Test]
        public void QueueAnswer_ForANonexistentQuestion_IsRejected()
        {
            (Simulation sim, RuleRunner runner, _) = Build();
            string rejection = runner.QueueAnswer(9999, 1);
            Assert.IsNotNull(rejection);
            StringAssert.Contains("does not exist", rejection);
        }

        [Test]
        public void QueueAnswer_WithAnEmptyChoice_IsRejected()
        {
            var rule = Rule("rule.ask_reject", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields(choice1: "A", choice2: "B", choice3: ""))
            }, new List<RuleConnection> { Conn("trigger", "then", "ask", "exec") });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            long questionId = runner.PendingQuestions[0].QuestionId;
            string rejection = runner.QueueAnswer(questionId, 3);

            Assert.IsNotNull(rejection);
            StringAssert.Contains("empty", rejection);
        }

        [Test]
        public void SimulationAnswerQuestion_ForANonexistentQuestion_IsRejected()
        {
            (Simulation sim, _, _) = Build();
            string rejection = sim.AnswerQuestion(9999, 1);
            Assert.IsNotNull(rejection);
        }

        // --- Withdrawal ---

        [Test]
        public void PendingQuestion_IsWithdrawnAndReported_WhenItsRuleIsSwitchedOff()
        {
            var sink = new TestLogSink();

            // "check" forces "read" to evaluate before asking; a missing "marker" parameter throws and switches the whole rule off.
            var rule = Rule("rule.ask_withdraw", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("read", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("marker") }),
                Node("check", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("checked") }),
                Node("ask", "block.flow.ask_player", AskFields())
            }, new List<RuleConnection>
            {
                Conn("trigger", "then", "check", "exec"),
                Conn("read", "result", "check", "arg0"),
                Conn("check", "then", "ask", "exec")
            });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(
                errorMode: ErrorMode.Release, logSink: sink, extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            sim.Events.Post("go", sim.Clock.CurrentTick, new EventData().SetDouble("marker", 1));
            sim.Step();
            Assert.AreEqual(1, runner.PendingQuestions.Count);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();

            Assert.IsFalse(runner.IsRuleActive("rule.ask_withdraw"));
            Assert.AreEqual(0, runner.PendingQuestions.Count, "the earlier question should have been withdrawn when the rule was switched off");
            Assert.IsTrue(runner.Reports.Any(r => r.Message.Contains("withdrawn")));
        }

        // --- Events ---

        [Test]
        public void QuestionEvents_CarryTheRightData()
        {
            var rule = Rule("rule.ask_events", new List<RuleNode>
            {
                Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                Node("ask", "block.flow.ask_player", AskFields())
            }, new List<RuleConnection> { Conn("trigger", "then", "ask", "exec") });

            (Simulation sim, RuleRunner runner, RuleCompileReport report) = Build(extraEvents: new[] { GoEvent }, rules: rule);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors));

            runner.RegisterQuestionDelegate(new FirstChoiceDelegate());

            PostedEvent asked = null;
            PostedEvent answered = null;
            sim.Events.Subscribe(RuleRunner.QuestionAskedEventId, posted => asked = posted);
            sim.Events.Subscribe(RuleRunner.QuestionAnsweredEventId, posted => answered = posted);

            sim.Events.Post("go", sim.Clock.CurrentTick);
            sim.Step();
            sim.Step(); // deliver

            Assert.IsNotNull(asked);
            Assert.IsNotNull(answered);

            asked.Data.TryGetDouble("questionId", out double askedQuestionId);
            asked.Data.TryGetString("ruleId", out string askedRuleId);
            asked.Data.TryGetString("category", out string askedCategory);
            Assert.AreEqual("rule.ask_events", askedRuleId);
            Assert.AreEqual("question.general", askedCategory);

            answered.Data.TryGetDouble("questionId", out double answeredQuestionId);
            answered.Data.TryGetDouble("choice", out double choice);
            answered.Data.TryGetString("answeredBy", out string answeredBy);
            Assert.AreEqual(askedQuestionId, answeredQuestionId);
            Assert.AreEqual(1.0, choice);
            Assert.AreEqual("Delegate", answeredBy);
        }

        // --- Save and load ---

        private static TempContentSource WriteQuestionTestSource()
        {
            var source = new TempContentSource("core");
            source.WriteDataFile("rules.json", @"{
                ""type"": ""rule"",
                ""items"": [
                    {
                        ""id"": ""rule.question_test"",
                        ""enabled"": true,
                        ""nodes"": [
                            { ""id"": ""trigger"", ""blockType"": ""block.trigger.on_event"", ""fields"": { ""event"": ""test.start"" } },
                            { ""id"": ""ask"", ""blockType"": ""block.flow.ask_player"", ""fields"": { ""title"": ""Q"", ""body"": ""B"", ""choice1"": ""A"", ""choice2"": ""B"", ""defaultChoice"": 2, ""timeoutHours"": 6 } },
                            { ""id"": ""change"", ""blockType"": ""block.action.change_counter"", ""fields"": { ""name"": ""test_counter"", ""amount"": 1 } },
                            { ""id"": ""finish"", ""blockType"": ""block.action.post_event"", ""fields"": { ""event"": ""test.finished"" } }
                        ],
                        ""connections"": [
                            { ""fromNode"": ""trigger"", ""fromPort"": ""then"", ""toNode"": ""ask"", ""toPort"": ""exec"" },
                            { ""fromNode"": ""ask"", ""fromPort"": ""choice2"", ""toNode"": ""change"", ""toPort"": ""exec"" },
                            { ""fromNode"": ""change"", ""fromPort"": ""then"", ""toNode"": ""finish"", ""toPort"": ""exec"" }
                        ]
                    }
                ]
            }");

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
        public void SaveLoad_WithAPendingQuestion_MatchesAnUnbrokenRun()
        {
            using TempContentSource unbrokenSource = WriteQuestionTestSource();
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

            using TempContentSource interruptedSource = WriteQuestionTestSource();
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

            Assert.AreEqual(1, day1.RuleRunner.PendingQuestions.Count, "sanity: the question should still be unanswered at the save point");

            using var stream = new MemoryStream();
            GameSave.Save(day1, new SaveHeaderInfo { KingdomName = "K", Population = 0 }, stream);
            stream.Position = 0;

            (Simulation day2, _) = GameSave.Load(stream, interruptedSources, null);
            day2.Events.Subscribe("test.finished", posted => interruptedFinished.Add((posted.Tick, posted.ChainDepth)));

            Assert.AreEqual(1, day2.RuleRunner.PendingQuestions.Count, "the pending question should have survived save and load");

            for (int i = 0; i < 9 * CalendarConstants.TicksPerHour; i++)
            {
                day2.Step();
            }

            ulong interruptedHash = day2.ComputeStateHash();
            double interruptedCounter = day2.Memory.GetCounter(EntityId.None, "test_counter");

            Assert.AreEqual(1.0, unbrokenCounter, "sanity: the timeout's default choice should have fired exactly once over the unbroken run");
            Assert.AreEqual(unbrokenCounter, interruptedCounter);
            CollectionAssert.AreEqual(unbrokenFinished, interruptedFinished);
            Assert.AreEqual(unbrokenHash, interruptedHash, "state hash after an interrupted run should match an unbroken run from the same seed.");
        }
    }
}
