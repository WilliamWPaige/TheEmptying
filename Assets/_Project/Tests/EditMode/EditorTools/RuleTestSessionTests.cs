using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Editing;
using TheEmptying.Editor.Rules.Testing;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.7: the rule editor's test tool - its throwaway sandbox simulation.</summary>
    public class RuleTestSessionTests
    {
        private static RuleNode Node(string id, string blockType, Dictionary<string, RuleFieldValueSource> fields = null) =>
            new RuleNode { Id = id, BlockTypeId = blockType, Fields = fields ?? new Dictionary<string, RuleFieldValueSource>() };

        private static RuleConnection Conn(string fromNode, string fromPort, string toNode, string toPort) =>
            new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };

        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));

        /// <summary>Declares the "go" event and the settings RuleRunner needs, matching the real core content's rules_settings.json, so SimulationBuilder.Build resolves them the same way the game does.</summary>
        private static void WriteRequiredContent(TempContentSource source)
        {
            source.WriteDataFile("manifest.json", "{\"type\":\"module_manifest\",\"items\":[]}");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""id"": ""go"", ""visibility"": ""Public"", ""reportingScore"": 0, ""notabilityScore"": 0, ""description"": ""test event"" }
                ]
            }");
            source.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""setting.rules.max_fires_per_tick"", ""valueType"": ""Integer"", ""default"": 1000, ""minimum"": 1, ""maximum"": 1000000, ""category"": ""Rules"", ""description"": ""x"" },
                    { ""id"": ""setting.rules.questions_pause_game"", ""valueType"": ""Boolean"", ""default"": 1, ""minimum"": 0, ""maximum"": 1, ""category"": ""Rules"", ""description"": ""x"" }
                ]
            }");
        }

        private static RuleDefinition OnEventLogRule(string id, string eventId, string logText, bool enabled = true) =>
            new RuleDefinition
            {
                Id = id,
                Name = id,
                Description = string.Empty,
                Enabled = enabled,
                Nodes = new List<RuleNode>
                {
                    Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit(eventId) }),
                    Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit(logText) })
                },
                Connections = new List<RuleConnection> { Conn("trigger", "then", "log", "exec") }
            };

        private static string LastLogLine(RuleTestSession session, string ruleId)
        {
            RuleTraceSegmentLogText(session, ruleId, out string text);
            return text;
        }

        private static void RuleTraceSegmentLogText(RuleTestSession session, string ruleId, out string text)
        {
            text = session.Segments
                .Where(s => s.RuleId == ruleId)
                .SelectMany(s => s.Entries)
                .Where(e => e.Kind == TheEmptying.Core.Rules.Tracing.RuleTraceEntryKind.LineLogged)
                .Select(e => e.Message)
                .LastOrDefault();
        }

        [Test]
        public void Build_UsesTheLiveRuleDefinition_EvenThoughTheOnDiskVersionDiffers_AndEvenThoughItIsDisabled()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            RuleDefinition onDiskOldVersion = OnEventLogRule("rule.live_test", "go", "old text", enabled: false);
            source.WriteDataFile("rules.json", RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { onDiskOldVersion }));

            RuleDefinition liveUnsavedVersion = OnEventLogRule("rule.live_test", "go", "new unsaved text", enabled: false);

            var session = new RuleTestSession();
            session.Build(liveUnsavedVersion, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: false, seed: 1);

            Assert.IsFalse(session.LastStartupReport.RuleErrors.Any(), string.Join("\n", session.LastStartupReport.RuleErrors));
            Assert.IsTrue(session.Runner.IsRuleActive("rule.live_test"), "the rule must run even though its own Enabled checkbox is off, since the test tool always runs the rule it's showing.");

            session.FireEvent("go", null);

            Assert.AreEqual("new unsaved text", LastLogLine(session, "rule.live_test"));
        }

        [Test]
        public void Isolated_OnlyTheLiveRuleFires_EvenWhenAnotherRuleSharesItsTrigger()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            RuleDefinition otherRule = OnEventLogRule("rule.other", "go", "other fired");
            source.WriteDataFile("rules.json", RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { otherRule }));

            RuleDefinition liveRule = OnEventLogRule("rule.live_isolated", "go", "live fired");

            var session = new RuleTestSession();
            session.Build(liveRule, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: true, seed: 1);

            Assert.IsTrue(session.Runner.IsRuleActive("rule.live_isolated"));
            Assert.IsFalse(session.Runner.IsRuleActive("rule.other"), "isolated mode must disable every other rule.");

            session.FireEvent("go", null);

            Assert.AreEqual("live fired", LastLogLine(session, "rule.live_isolated"));
        }

        [Test]
        public void NotIsolated_OtherEnabledRulesAlsoRun()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            RuleDefinition otherRule = OnEventLogRule("rule.other2", "go", "other fired");
            source.WriteDataFile("rules.json", RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { otherRule }));

            RuleDefinition liveRule = OnEventLogRule("rule.live_all", "go", "live fired");

            var session = new RuleTestSession();
            session.Build(liveRule, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: false, seed: 1);

            Assert.IsTrue(session.Runner.IsRuleActive("rule.live_all"));
            Assert.IsTrue(session.Runner.IsRuleActive("rule.other2"), "with 'all rules', another enabled rule must stay active so chains can be seen.");
        }

        [Test]
        public void FireEvent_WithParameters_DeliversThemToTheRule()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            var liveRule = new RuleDefinition
            {
                Id = "rule.params_test",
                Name = "rule.params_test",
                Description = string.Empty,
                Enabled = true,
                Nodes = new List<RuleNode>
                {
                    Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                    Node("read", "block.value.read_event_parameter.number", new Dictionary<string, RuleFieldValueSource> { ["parameter"] = Lit("amount") }),
                    Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("{0}") })
                },
                Connections = new List<RuleConnection> { Conn("trigger", "then", "log", "exec"), Conn("read", "result", "log", "arg0") }
            };

            var session = new RuleTestSession();
            session.Build(liveRule, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: true, seed: 1);

            session.FireEvent("go", new[] { ("amount", RuleDataType.Number, (object)42.5) });

            Assert.AreEqual("42.5", LastLogLine(session, "rule.params_test"));
        }

        [Test]
        public void FireNow_FiresATimeTrigger()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            var liveRule = new RuleDefinition
            {
                Id = "rule.fire_now_test",
                Name = "rule.fire_now_test",
                Description = string.Empty,
                Enabled = true,
                Nodes = new List<RuleNode>
                {
                    Node("trigger", "block.trigger.on_new_day"),
                    Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("fired now") })
                },
                Connections = new List<RuleConnection> { Conn("trigger", "then", "log", "exec") }
            };

            var session = new RuleTestSession();
            session.Build(liveRule, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: true, seed: 1);

            session.FireNow();

            Assert.AreEqual("fired now", LastLogLine(session, "rule.fire_now_test"));
        }

        [Test]
        public void SkipToResume_AdvancesExactlyToTheWaitsResumeTick_AndContinues()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            var liveRule = new RuleDefinition
            {
                Id = "rule.wait_test",
                Name = "rule.wait_test",
                Description = string.Empty,
                Enabled = true,
                Nodes = new List<RuleNode>
                {
                    Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                    Node("wait", "block.flow.wait", new Dictionary<string, RuleFieldValueSource> { ["amount"] = Lit(1.0), ["unit"] = Lit("hours") }),
                    Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("resumed") })
                },
                Connections = new List<RuleConnection> { Conn("trigger", "then", "wait", "exec"), Conn("wait", "then", "log", "exec") }
            };

            var session = new RuleTestSession();
            session.Build(liveRule, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: true, seed: 1);

            session.FireEvent("go", null);

            Assert.IsTrue(session.TryGetResumeTick(out long resumeTick));
            Assert.IsNull(LastLogLine(session, "rule.wait_test"), "must not have logged yet - it's waiting.");

            session.SkipToResume();

            Assert.AreEqual(resumeTick, session.Simulation.Clock.CurrentTick);
            Assert.AreEqual("resumed", LastLogLine(session, "rule.wait_test"));
        }

        [Test]
        public void AnswerQuestion_ContinuesAlongTheChosenOutput()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            var liveRule = new RuleDefinition
            {
                Id = "rule.ask_test",
                Name = "rule.ask_test",
                Description = string.Empty,
                Enabled = true,
                Nodes = new List<RuleNode>
                {
                    Node("trigger", "block.trigger.on_event", new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") }),
                    Node("ask", "block.flow.ask_player", new Dictionary<string, RuleFieldValueSource> { ["choice1"] = Lit("A"), ["choice2"] = Lit("B") }),
                    Node("log", "block.action.write_log_line", new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("chose B") })
                },
                Connections = new List<RuleConnection> { Conn("trigger", "then", "ask", "exec"), Conn("ask", "choice2", "log", "exec") }
            };

            var session = new RuleTestSession();
            session.Build(liveRule, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: true, seed: 1);

            session.FireEvent("go", null);

            List<TheEmptying.Core.Rules.QuestionInfo> pending = session.PendingQuestionsForRule().ToList();
            Assert.AreEqual(1, pending.Count);

            session.AnswerQuestion(pending[0].QuestionId, 2);

            Assert.AreEqual("chose B", LastLogLine(session, "rule.ask_test"));
        }

        [Test]
        public void Clear_ResetsTheTraceAndRunsCleanAgain()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            RuleDefinition liveRule = OnEventLogRule("rule.clear_test", "go", "fired");

            var session = new RuleTestSession();
            session.Build(liveRule, new[] { source.FolderPath }, Array.Empty<IModule>(), isolated: true, seed: 1);

            session.FireEvent("go", null);
            Assert.AreEqual(1, session.Segments.Count);

            session.Clear();

            Assert.AreEqual(0, session.Segments.Count);
            Assert.AreEqual(0, session.Simulation.Clock.CurrentTick, "a fresh throwaway simulation should start at tick 0 again.");

            session.FireEvent("go", null);
            Assert.AreEqual(1, session.Segments.Count);
            Assert.AreEqual("fired", LastLogLine(session, "rule.clear_test"));
        }
    }
}
