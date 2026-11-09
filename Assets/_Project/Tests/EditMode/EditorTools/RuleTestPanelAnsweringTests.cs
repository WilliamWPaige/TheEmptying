using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules;
using TheEmptying.Editor.Rules.Editing;
using TheEmptying.Editor.Rules.Testing;
using TheEmptying.Editor.Rules.Testing.UI;
using TheEmptying.Tests.EditMode.Content;
using UnityEngine.UIElements;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>
    /// Prompt 2.7b: hand-testing found that after firing a rule whose Ask
    /// Player ended the run "asking question 1", no question or choice
    /// buttons appeared. The cause was that the Fire and Fire Now button
    /// handlers refreshed the run view but never rebuilt the fire/waiting/
    /// asking controls host, so a question that only became pending as a
    /// result of that very fire was never shown. This exercises the real Test
    /// tab handlers (made internal so a test can call them directly, since a
    /// Button's click can only be simulated with a live panel) end to end.
    /// </summary>
    public class RuleTestPanelAnsweringTests
    {
        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));

        private static void WriteRequiredContent(TempContentSource source)
        {
            // module.demo_hunger must have a manifest entry (ModuleCatalog.CreateModules
            // includes it) for RuleTestSession.Build's real SimulationBuilder.Build call
            // to succeed; disabled, so it needs nothing beyond the entry itself. The same
            // manifest also feeds EditorContext.Build below, so both calls agree.
            source.WriteDataFile("manifest.json", "{\"type\":\"module_manifest\",\"items\":[{\"id\":\"module.demo_hunger\",\"version\":1,\"dependencies\":[],\"enabled\":false}]}");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [ { ""id"": ""go"", ""visibility"": ""Public"", ""reportingScore"": 0, ""notabilityScore"": 0, ""description"": ""test event"" } ]
            }");
            source.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""setting.rules.max_fires_per_tick"", ""valueType"": ""Integer"", ""default"": 1000, ""minimum"": 1, ""maximum"": 1000000, ""category"": ""Rules"", ""description"": ""x"" },
                    { ""id"": ""setting.rules.questions_pause_game"", ""valueType"": ""Boolean"", ""default"": 1, ""minimum"": 0, ""maximum"": 1, ""category"": ""Rules"", ""description"": ""x"" }
                ]
            }");
        }

        private static bool AnyLabelContains(VisualElement root, string substring)
        {
            var labels = new List<Label>();
            root.Query<Label>().ToList(labels);
            return labels.Any(l => l.text != null && l.text.Contains(substring));
        }

        private static bool AnyButtonWithText(VisualElement root, string text)
        {
            var buttons = new List<Button>();
            root.Query<Button>().ToList(buttons);
            return buttons.Any(b => b.text == text);
        }

        [Test]
        public void WaitingForAnAnswer_ShowsQuestionAndChoiceButtons_AndAnsweringContinuesTheRun()
        {
            using var source = new TempContentSource("core");
            WriteRequiredContent(source);

            // Modules omitted (not Array.Empty<IModule>()) so this matches ModuleCatalog.CreateModules(),
            // the same real module list the "Build" button's RuleTestSession.Build uses.
            EditorContext context = EditorContext.Build(new[] { source.FolderPath });

            var rule = new RuleDefinition
            {
                Id = "rule.ask_ui_test", Name = "rule.ask_ui_test", Description = string.Empty, Enabled = true,
                Nodes = new List<RuleNode>
                {
                    new RuleNode { Id = "trigger", BlockTypeId = "block.trigger.on_event", Fields = new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("go") } },
                    new RuleNode { Id = "ask", BlockTypeId = "block.flow.ask_player", Fields = new Dictionary<string, RuleFieldValueSource> { ["title"] = Lit("Test 1"), ["choice1"] = Lit("A"), ["choice2"] = Lit("B") } },
                    new RuleNode { Id = "log", BlockTypeId = "block.action.write_log_line", Fields = new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("chose B") } }
                },
                Connections = new List<RuleConnection>
                {
                    new RuleConnection { FromNode = "trigger", FromPort = "then", ToNode = "ask", ToPort = "exec" },
                    new RuleConnection { FromNode = "ask", FromPort = "choice2", ToNode = "log", ToPort = "exec" }
                }
            };

            var model = new RuleEditorModel(rule, context);
            var panel = new RuleTestPanel(model);

            panel.OnBuildClicked();
            panel.OnFireEventClicked();

            Assert.IsTrue(AnyLabelContains(panel, "Test 1"), "the question's title must be shown once a run is waiting for an answer.");
            Assert.IsTrue(AnyButtonWithText(panel, "A"), "a button for each filled choice must be shown.");
            Assert.IsTrue(AnyButtonWithText(panel, "B"), "a button for each filled choice must be shown.");

            QuestionInfo question = panel.SessionForTests.PendingQuestionsForRule().Single();
            panel.OnAnswerChoiceClicked(question.QuestionId, 2);

            Assert.IsTrue(AnyLabelContains(panel, "chose B"), "answering must continue the run along the chosen output.");
        }
    }
}
