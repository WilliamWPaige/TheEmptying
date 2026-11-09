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
using TheEmptying.Editor.Rules.Canvas;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.7b: the canvas highlighter's data (execution order, followed wires, port values, error node) lives in RuleHighlightData, a pure view-model, so it's tested without a window.</summary>
    public class RuleHighlightDataTests
    {
        private sealed class RecordingTraceListener : IRuleTraceListener
        {
            public List<RuleTraceSegment> Segments { get; } = new List<RuleTraceSegment>();
            public void OnSegmentCompleted(RuleTraceSegment segment) => Segments.Add(segment);
        }

        private static RuleNode Node(string id, string blockType, Dictionary<string, RuleFieldValueSource> fields = null) =>
            new RuleNode { Id = id, BlockTypeId = blockType, Fields = fields ?? new Dictionary<string, RuleFieldValueSource>() };

        private static RuleConnection Conn(string fromNode, string fromPort, string toNode, string toPort) =>
            new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };

        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));

        private static RuleTraceSegment RunRuleAndCaptureSegment(RuleDefinition rule, string triggerEventId = "go")
        {
            var simulation = new Simulation(1, null, ErrorMode.Development);
            CoreBlocks.RegisterAll(simulation.Blocks);
            simulation.Settings.Declare(new SettingDefinition
            {
                Id = RuleRunner.MaxFiresPerTickSettingId, ValueType = SettingValueType.Integer, Default = 1000, Minimum = 1, Maximum = 1000000, Category = "Rules", Description = "x"
            });
            simulation.EventDefinitions.Register(new EventDefinition(triggerEventId, EventVisibility.Public, 0, 0, "x"));

            var database = new ContentDatabase();
            database.Set(BuiltInContentTypes.RuleTypeName, rule, "core", "rules.json");

            (RuleRegistry registry, RuleCompileReport report) = RuleCompiler.Compile(database, simulation.Blocks, simulation.Settings, simulation.EventDefinitions, ErrorMode.Development);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Errors.Select(e => e.ToString())));

            var runner = new RuleRunner(registry, simulation.Events, simulation.Scheduler, simulation.Random, simulation.Settings, simulation.EventDefinitions, simulation.Memory, null, ErrorMode.Development, () => simulation.Clock.CurrentTick);
            runner.Activate();
            simulation.RuleRunner = runner;

            var listener = new RecordingTraceListener();
            runner.SetTraceListener(rule.Id, listener);

            simulation.Events.Post(triggerEventId, simulation.Clock.CurrentTick);
            simulation.Step();

            Assert.AreEqual(1, listener.Segments.Count);
            return listener.Segments[0];
        }

        [Test]
        public void TriggerAndBranchAndSequenceAndWait_ProduceCorrectOrderFollowedWiresAndValues()
        {
            // trigger -> branch(true) -> seq -> then1: log1, then2: wait
            var rule = new RuleDefinition
            {
                Id = "rule.highlight_test", Name = "rule.highlight_test", Description = string.Empty, Enabled = true,
                Nodes = new List<RuleNode>
                {
                    Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                    Node("branch", "block.flow.branch", new Dictionary<string, RuleFieldValueSource> { ["condition"] = Lit(true) }),
                    Node("seq", "block.flow.sequence"),
                    Node("log1", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("one") }),
                    Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(1.0), ["unit"] = Lit("hours") })
                },
                Connections = new List<RuleConnection>
                {
                    Conn("trigger", "then", "branch", "exec"),
                    Conn("branch", "true", "seq", "exec"),
                    Conn("seq", "then1", "log1", "exec"),
                    Conn("seq", "then2", "wait", "exec")
                }
            };

            RuleTraceSegment segment = RunRuleAndCaptureSegment(rule);
            Assert.AreEqual(RuleTraceEndKind.Waiting, segment.EndKind);

            RuleHighlightData data = RuleHighlightData.Build(segment);

            // Execution order: trigger, branch, seq, log1, wait - in that order, 1-based.
            Assert.AreEqual(1, data.ExecutionOrderByNode["trigger"]);
            Assert.AreEqual(2, data.ExecutionOrderByNode["branch"]);
            Assert.AreEqual(3, data.ExecutionOrderByNode["seq"]);
            Assert.AreEqual(4, data.ExecutionOrderByNode["log1"]);
            Assert.AreEqual(5, data.ExecutionOrderByNode["wait"]);

            // Every single-output wire actually followed, including the trigger's own
            // (a Sequence's own fan-out isn't a single followed port - it records no
            // port for itself and runs every branch in turn - so it isn't expected here).
            CollectionAssert.Contains(data.FollowedExecutionOutputs, ("trigger", "then"));
            CollectionAssert.Contains(data.FollowedExecutionOutputs, ("branch", "true"));

            // The branch's own condition input value is recorded against its port name.
            List<(string Port, string Value)> branchInputs = data.InputValuesByNode["branch"];
            Assert.IsTrue(branchInputs.Any(v => v.Port == "condition" && v.Value == "true"));

            Assert.IsNull(data.ErrorNodeId);
        }

        [Test]
        public void NullSegment_ProducesEmptyUnhighlightedData()
        {
            RuleHighlightData data = RuleHighlightData.Build(null);

            Assert.AreEqual(0, data.ExecutionOrderByNode.Count);
            Assert.AreEqual(0, data.FollowedExecutionOutputs.Count);
            Assert.AreEqual(0, data.InputValuesByNode.Count);
            Assert.AreEqual(0, data.ComputedValueByNode.Count);
            Assert.IsNull(data.ErrorNodeId);
        }
    }
}
