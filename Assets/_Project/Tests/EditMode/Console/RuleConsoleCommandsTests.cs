using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;

namespace TheEmptying.Tests.EditMode.Console
{
    /// <summary>Prompt 2.7: the "rules" and "trace" console commands.</summary>
    public class RuleConsoleCommandsTests
    {
        private sealed class CapturingLogSink : ILogSink
        {
            public List<string> Lines { get; } = new List<string>();
            public void Log(string message) => Lines.Add(message);
        }

        private sealed class CapturingPresentationAndLog : IPresentationCommands, ILogSink
        {
            public List<string> Lines { get; } = new List<string>();
            public bool IsPaused { get; private set; }
            public int Speed { get; private set; } = 1;
            public void SetPaused(bool paused) => IsPaused = paused;
            public void SetSpeed(int speed) => Speed = speed;
            public string Save(string name) => "saved";
            public string Load(string name) => "loaded";
            public string ListSaves() => "(none)";
            public string SetLogFilter(string mode) => mode;
            public void Log(string message) => Lines.Add(message);
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

        private static (Simulation Simulation, RuleRunner Runner) BuildWithOneRule()
        {
            var simulation = new Simulation(1, null, ErrorMode.Development);
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

            simulation.EventDefinitions.Register(new EventDefinition("go", EventVisibility.Public, 0, 0, "x"));

            var rule = new RuleDefinition
            {
                Id = "rule.console_test",
                Name = "rule.console_test",
                Description = string.Empty,
                Enabled = true,
                Nodes = new List<RuleNode>
                {
                    Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                    Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("fired") })
                },
                Connections = new List<RuleConnection> { Conn("trigger", "then", "log", "exec") }
            };

            var database = new ContentDatabase();
            database.Set(BuiltInContentTypes.RuleTypeName, rule, "core", "rules.json");

            (RuleRegistry registry, RuleCompileReport report) = RuleCompiler.Compile(database, simulation.Blocks, simulation.Settings, simulation.EventDefinitions, ErrorMode.Development);
            Assert.IsFalse(report.HasErrors);

            var runner = new RuleRunner(registry, simulation.Events, simulation.Scheduler, simulation.Random, simulation.Settings, simulation.EventDefinitions, simulation.Memory, null, ErrorMode.Development, () => simulation.Clock.CurrentTick);
            runner.Activate();
            simulation.RuleRunner = runner;

            return (simulation, runner);
        }

        [Test]
        public void Rules_ListsActivatedRules_WithFiresPausedAndPendingCounts()
        {
            (Simulation simulation, _) = BuildWithOneRule();
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            string before = simulation.Commands.Execute("rules");
            StringAssert.Contains("rule.console_test: active, fires 0, paused 0, pending questions 0", before);

            simulation.Events.Post("go", simulation.Clock.CurrentTick);
            simulation.Step();

            string after = simulation.Commands.Execute("rules");
            StringAssert.Contains("fires 1", after);
        }

        [Test]
        public void Trace_On_WritesSegmentsToTheLog_AndOffStopsIt()
        {
            (Simulation simulation, RuleRunner runner) = BuildWithOneRule();
            var presentationLog = new CapturingPresentationAndLog();
            BuiltInConsoleCommands.RegisterAll(simulation, presentationLog);

            string onResult = simulation.Commands.Execute("trace rule.console_test on");
            StringAssert.Contains("on", onResult);
            Assert.IsTrue(runner.IsTracingEnabled("rule.console_test"));

            simulation.Events.Post("go", simulation.Clock.CurrentTick);
            simulation.Step();

            Assert.IsTrue(presentationLog.Lines.Exists(l => l.Contains("[trace]") && l.Contains("rule.console_test")));

            string offResult = simulation.Commands.Execute("trace rule.console_test off");
            StringAssert.Contains("off", offResult);
            Assert.IsFalse(runner.IsTracingEnabled("rule.console_test"));

            presentationLog.Lines.Clear();
            simulation.Events.Post("go", simulation.Clock.CurrentTick);
            simulation.Step();

            Assert.IsFalse(presentationLog.Lines.Exists(l => l.Contains("[trace]")), "no further trace lines after turning tracing off.");
        }

        [Test]
        public void Trace_On_WithAnExplicitLogSinkArgument_UsesThatSinkInsteadOfThePresentation()
        {
            (Simulation simulation, _) = BuildWithOneRule();
            var dedicatedLog = new CapturingLogSink();
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands(), dedicatedLog);

            simulation.Commands.Execute("trace rule.console_test on");
            simulation.Events.Post("go", simulation.Clock.CurrentTick);
            simulation.Step();

            Assert.IsTrue(dedicatedLog.Lines.Exists(l => l.Contains("[trace]")));
        }

        [Test]
        public void Trace_WithBadArguments_ShowsUsage()
        {
            (Simulation simulation, _) = BuildWithOneRule();
            BuiltInConsoleCommands.RegisterAll(simulation, new FakePresentationCommands());

            Assert.AreEqual("Usage: trace <ruleId> <on|off>", simulation.Commands.Execute("trace rule.console_test"));
            Assert.AreEqual("Usage: trace <ruleId> <on|off>", simulation.Commands.Execute("trace rule.console_test sideways"));
        }
    }
}
