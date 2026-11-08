using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules;
using TheEmptying.Editor.Rules.Canvas;
using TheEmptying.Editor.Rules.Editing;
using TheEmptying.Tests.EditMode.Content;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.6: the rule editor model - edits, undo/redo, connections, variant swap, copy/paste, stable saving, validation.</summary>
    public class RuleEditorModelTests
    {
        private static RuleNode Node(string id, string blockType, double x = 0, double y = 0, Dictionary<string, RuleFieldValueSource> fields = null)
        {
            return new RuleNode { Id = id, BlockTypeId = blockType, X = x, Y = y, Fields = fields ?? new Dictionary<string, RuleFieldValueSource>() };
        }

        private static RuleConnection Conn(string fromNode, string fromPort, string toNode, string toPort)
        {
            return new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };
        }

        private static RuleFieldValueSource Lit(object value) => RuleFieldValueSource.FromLiteral(JToken.FromObject(value));
        private static RuleFieldValueSource SettingRef(string settingId) => RuleFieldValueSource.FromSetting(settingId);

        private static RuleDefinition NewRule(string id = "rule.test") =>
            new RuleDefinition { Id = id, Name = id, Description = string.Empty, Enabled = true, Nodes = new List<RuleNode>(), Connections = new List<RuleConnection>() };

        private static EditorContext BuildContext()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", "{\"type\":\"module_manifest\",\"items\":[]}");
            return EditorContext.Build(new[] { source.FolderPath }, Array.Empty<IModule>());
        }

        // --- AddNode / DeleteNodes ---

        [Test]
        public void AddNode_AllocatesAShortUniqueId_AndIsUndoRedoable()
        {
            var model = new RuleEditorModel(NewRule(), BuildContext());

            string id = model.AddNode("block.flow.stop", new Vector2(10, 20));

            Assert.AreEqual("n1", id);
            Assert.AreEqual(1, model.Rule.Nodes.Count);
            Assert.AreEqual("block.flow.stop", model.Rule.Nodes[0].BlockTypeId);
            Assert.IsTrue(model.IsDirty);
            Assert.IsTrue(model.CanUndo);

            model.Undo();
            Assert.AreEqual(0, model.Rule.Nodes.Count);
            Assert.IsTrue(model.CanRedo);

            model.Redo();
            Assert.AreEqual(1, model.Rule.Nodes.Count);
            Assert.AreEqual("n1", model.Rule.Nodes[0].Id);
        }

        [Test]
        public void AllocateNodeId_PicksOneMoreThanTheHighestExistingNumber()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("n1", "block.flow.stop"));
            rule.Nodes.Add(Node("n3", "block.flow.stop"));
            var model = new RuleEditorModel(rule, BuildContext());

            Assert.AreEqual("n4", model.AllocateNodeId(), "never reuses a lower free number, so a deleted node's old Id can't resurface on an unrelated new node.");
        }

        [Test]
        public void DeleteNodes_RemovesNodesAndTheirWires_Undo_Redo()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("a", "block.flow.stop"));
            rule.Nodes.Add(Node("b", "block.flow.stop"));
            rule.Connections.Add(Conn("a", "exec", "b", "exec"));
            var model = new RuleEditorModel(rule, BuildContext());

            model.DeleteNodes(new[] { "a" });

            Assert.AreEqual(1, model.Rule.Nodes.Count);
            Assert.AreEqual("b", model.Rule.Nodes[0].Id);
            Assert.AreEqual(0, model.Rule.Connections.Count, "a wire touching a deleted node must go too.");

            model.Undo();
            Assert.AreEqual(2, model.Rule.Nodes.Count);
            Assert.AreEqual(1, model.Rule.Connections.Count);

            model.Redo();
            Assert.AreEqual(1, model.Rule.Nodes.Count);
            Assert.AreEqual(0, model.Rule.Connections.Count);
        }

        // --- Connection rules ---

        [Test]
        public void CanConnect_ExecToExec_IsAllowed()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("trigger", "block.trigger.on_event", fields: new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("event.herald_line") }));
            rule.Nodes.Add(Node("log", "block.action.write_log_line"));
            var model = new RuleEditorModel(rule, BuildContext());

            Assert.IsTrue(model.CanConnect("trigger", "then", "log", "exec").IsAllowed);
        }

        [Test]
        public void CanConnect_DataToDataSameType_IsAllowed()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("math1", "block.value.math"));
            rule.Nodes.Add(Node("math2", "block.value.math"));
            var model = new RuleEditorModel(rule, BuildContext());

            Assert.IsTrue(model.CanConnect("math1", "result", "math2", "a").IsAllowed);
        }

        [Test]
        public void CanConnect_WildcardInput_AcceptsAnyType()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("math", "block.value.math"));
            rule.Nodes.Add(Node("log", "block.action.write_log_line"));
            var model = new RuleEditorModel(rule, BuildContext());

            Assert.IsTrue(model.CanConnect("math", "result", "log", "arg0").IsAllowed);
        }

        [Test]
        public void CanConnect_KindMismatch_IsRejected()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("trigger", "block.trigger.on_event", fields: new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("event.herald_line") }));
            rule.Nodes.Add(Node("math", "block.value.math"));
            var model = new RuleEditorModel(rule, BuildContext());

            ConnectionCheckResult result = model.CanConnect("trigger", "then", "math", "a");
            Assert.IsFalse(result.IsAllowed);
        }

        [Test]
        public void CanConnect_DataTypeMismatch_IsRejected()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("math", "block.value.math")); // result: Number
            rule.Nodes.Add(Node("compareText", "block.value.compare_text")); // a: Text, required
            var model = new RuleEditorModel(rule, BuildContext());

            ConnectionCheckResult result = model.CanConnect("math", "result", "compareText", "a");
            Assert.IsFalse(result.IsAllowed);
        }

        [Test]
        public void Connect_ReplacesExistingWireAtAnInput()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("math1", "block.value.math"));
            rule.Nodes.Add(Node("math2", "block.value.math"));
            rule.Nodes.Add(Node("math3", "block.value.math"));
            var model = new RuleEditorModel(rule, BuildContext());

            model.Connect("math1", "result", "math3", "a");
            model.Connect("math2", "result", "math3", "a");

            List<RuleConnection> wiresIntoA = model.Rule.Connections.Where(c => c.ToNode == "math3" && c.ToPort == "a").ToList();
            Assert.AreEqual(1, wiresIntoA.Count);
            Assert.AreEqual("math2", wiresIntoA[0].FromNode);
        }

        [Test]
        public void Connect_ReplacesExistingWireAtAnExecutionOutput_ButADataOutputFansOutFreely()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("trigger", "block.trigger.on_event", fields: new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("event.herald_line") }));
            rule.Nodes.Add(Node("log1", "block.action.write_log_line"));
            rule.Nodes.Add(Node("log2", "block.action.write_log_line"));
            rule.Nodes.Add(Node("math", "block.value.math"));
            var model = new RuleEditorModel(rule, BuildContext());

            model.Connect("trigger", "then", "log1", "exec");
            model.Connect("trigger", "then", "log2", "exec");
            List<RuleConnection> fromTrigger = model.Rule.Connections.Where(c => c.FromNode == "trigger" && c.FromPort == "then").ToList();
            Assert.AreEqual(1, fromTrigger.Count, "an execution output may only connect to one input; the new wire replaces the old one.");
            Assert.AreEqual("log2", fromTrigger[0].ToNode);

            model.Connect("math", "result", "log1", "arg0");
            model.Connect("math", "result", "log2", "arg0");
            List<RuleConnection> fromMath = model.Rule.Connections.Where(c => c.FromNode == "math" && c.FromPort == "result").ToList();
            Assert.AreEqual(2, fromMath.Count, "a data output may fan out to many inputs.");
        }

        [Test]
        public void Connect_Undo_Redo()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("math1", "block.value.math"));
            rule.Nodes.Add(Node("math2", "block.value.math"));
            var model = new RuleEditorModel(rule, BuildContext());

            model.Connect("math1", "result", "math2", "a");
            Assert.AreEqual(1, model.Rule.Connections.Count);

            model.Undo();
            Assert.AreEqual(0, model.Rule.Connections.Count);

            model.Redo();
            Assert.AreEqual(1, model.Rule.Connections.Count);
        }

        [Test]
        public void Disconnect_RemovesOneWire_Undo_Redo()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("math1", "block.value.math"));
            rule.Nodes.Add(Node("math2", "block.value.math"));
            var model = new RuleEditorModel(rule, BuildContext());
            RuleConnection connection = model.Connect("math1", "result", "math2", "a");

            model.Disconnect(connection);
            Assert.AreEqual(0, model.Rule.Connections.Count);

            model.Undo();
            Assert.AreEqual(1, model.Rule.Connections.Count);

            model.Redo();
            Assert.AreEqual(0, model.Rule.Connections.Count);
        }

        [Test]
        public void DisconnectAllAtPort_RemovesEveryWireThere_Undo_Redo()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("math", "block.value.math"));
            rule.Nodes.Add(Node("log1", "block.action.write_log_line"));
            rule.Nodes.Add(Node("log2", "block.action.write_log_line"));
            var model = new RuleEditorModel(rule, BuildContext());
            model.Connect("math", "result", "log1", "arg0");
            model.Connect("math", "result", "log2", "arg0");
            Assert.AreEqual(2, model.Rule.Connections.Count);

            model.DisconnectAllAtPort("math", "result", PortDirection.Output);
            Assert.AreEqual(0, model.Rule.Connections.Count);

            model.Undo();
            Assert.AreEqual(2, model.Rule.Connections.Count);

            model.Redo();
            Assert.AreEqual(0, model.Rule.Connections.Count);
        }

        // --- Family variant swap ---

        [Test]
        public void SwapVariant_KeepsFittingWires_RemovesTheRest_Undo_Redo()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("getLocal", "block.value.get_local.number", fields: new Dictionary<string, RuleFieldValueSource> { ["name"] = Lit("x") }));
            rule.Nodes.Add(Node("math", "block.value.math"));
            rule.Nodes.Add(Node("log", "block.action.write_log_line"));
            rule.Connections.Add(Conn("getLocal", "result", "math", "a"));
            rule.Connections.Add(Conn("getLocal", "result", "log", "arg0"));
            var model = new RuleEditorModel(rule, BuildContext());

            VariantSwapResult result = model.SwapVariant("getLocal", "block.value.get_local.text");

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("block.value.get_local.text", model.Rule.Nodes.First(n => n.Id == "getLocal").BlockTypeId);
            Assert.AreEqual(1, result.RemovedConnections.Count);
            Assert.AreEqual("math", result.RemovedConnections[0].ToNode, "Number no longer matches Math's Number input, so that wire is removed.");
            Assert.AreEqual(1, model.Rule.Connections.Count);
            Assert.AreEqual("log", model.Rule.Connections[0].ToNode, "the wildcard input still fits any type, so that wire is kept.");

            model.Undo();
            Assert.AreEqual("block.value.get_local.number", model.Rule.Nodes.First(n => n.Id == "getLocal").BlockTypeId);
            Assert.AreEqual(2, model.Rule.Connections.Count);

            model.Redo();
            Assert.AreEqual("block.value.get_local.text", model.Rule.Nodes.First(n => n.Id == "getLocal").BlockTypeId);
            Assert.AreEqual(1, model.Rule.Connections.Count);
        }

        [Test]
        public void BlockFamilyCatalog_DetectsGetLocalSetLocalAndReadEventParameter_WithOneVariantPerType()
        {
            EditorContext context = BuildContext();
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);

            BlockFamily getLocal = families.FirstOrDefault(f => f.FamilyKey == "block.value.get_local");
            Assert.IsNotNull(getLocal);
            Assert.AreEqual("Get Local", getLocal.DisplayName);
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(RuleDataType)), getLocal.VariantsByType.Keys);

            Assert.IsTrue(BlockFamilyCatalog.TryGetFamilyForBlockType(families, "block.value.get_local.number", out BlockFamily found));
            Assert.AreEqual("block.value.get_local", found.FamilyKey);
        }

        // --- Combined edits ---

        [Test]
        public void DragSelected_SeveralCallsInOneCombinedEdit_IsOneUndoStep()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("a", "block.flow.stop", 0, 0));
            var model = new RuleEditorModel(rule, BuildContext());
            model.Canvas.SelectNodeById("a", additive: false);

            model.BeginCombinedEdit();
            model.DragSelected(new Vector2(5, 0));
            model.DragSelected(new Vector2(5, 0));
            model.DragSelected(new Vector2(5, 0));
            model.EndCombinedEdit();

            Assert.AreEqual(15.0, model.Rule.Nodes[0].X, 0.001);
            Assert.AreEqual(1, model.CanUndo ? 1 : 0, "should have pushed exactly one undo entry for the whole drag.");

            model.Undo();
            // Undo restores by replacing Rule.Nodes with freshly-cloned nodes, so re-read rather than reuse any RuleNode reference captured before the undo.
            Assert.AreEqual(0.0, model.Rule.Nodes[0].X, 0.001, "undo should jump back to before the whole drag, in one step.");
            Assert.IsFalse(model.CanUndo);
        }

        [Test]
        public void SetFieldValue_SeveralCallsInOneCombinedEdit_IsOneUndoStep()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("log", "block.action.write_log_line"));
            var model = new RuleEditorModel(rule, BuildContext());

            model.BeginCombinedEdit();
            model.SetFieldValue("log", "text", Lit("h"));
            model.SetFieldValue("log", "text", Lit("he"));
            model.SetFieldValue("log", "text", Lit("hello"));
            model.EndCombinedEdit();

            Assert.AreEqual("hello", model.Rule.Nodes[0].Fields["text"].Literal.Value<string>());

            model.Undo();
            Assert.IsFalse(model.Rule.Nodes[0].Fields.ContainsKey("text"), "undo should jump back to before any of the typing happened.");
            Assert.IsFalse(model.CanUndo);

            model.Redo();
            Assert.AreEqual("hello", model.Rule.Nodes[0].Fields["text"].Literal.Value<string>());
        }

        [Test]
        public void CombinedEdit_WithNoActualChange_PushesNoUndoStep()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("a", "block.flow.stop", 0, 0));
            var model = new RuleEditorModel(rule, BuildContext());
            model.Canvas.SelectNodeById("a", additive: false);

            model.BeginCombinedEdit();
            model.DragSelected(Vector2.zero);
            model.EndCombinedEdit();

            Assert.IsFalse(model.CanUndo, "a no-op combined edit should not leave an undo step behind.");
        }

        [Test]
        public void SetRuleProperties_Combined_IsOneUndoStep()
        {
            var model = new RuleEditorModel(NewRule("rule.original"), BuildContext());

            model.BeginCombinedEdit();
            model.SetRuleProperties("rule.orig", "Orig", "", true);
            model.SetRuleProperties("rule.origi", "Origi", "", true);
            model.SetRuleProperties("rule.original2", "Original2", "a description", false);
            model.EndCombinedEdit();

            Assert.AreEqual("rule.original2", model.Rule.Id);

            model.Undo();
            Assert.AreEqual("rule.original", model.Rule.Id);
            Assert.IsFalse(model.CanUndo);
        }

        // --- Copy, paste, duplicate ---

        [Test]
        public void CopyPaste_AssignsNewIds_KeepsInternalWires_DropsWiresToUnselectedNodes()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("a", "block.value.math", 0, 0));
            rule.Nodes.Add(Node("b", "block.value.math", 200, 0));
            rule.Nodes.Add(Node("c", "block.value.math", 400, 0)); // not selected
            rule.Connections.Add(Conn("a", "result", "b", "a")); // internal (both selected)
            rule.Connections.Add(Conn("b", "result", "c", "a")); // to an unselected node
            var model = new RuleEditorModel(rule, BuildContext());

            model.Canvas.SelectNodeById("a", additive: false);
            model.Canvas.SelectNodeById("b", additive: true);

            IReadOnlyList<string> newIds = model.PasteFromClipboardText(model.CopySelectionToClipboardText(), new Vector2(1000, 1000));

            Assert.AreEqual(2, newIds.Count);
            CollectionAssert.DoesNotContain(newIds, "a");
            CollectionAssert.DoesNotContain(newIds, "b");
            Assert.AreEqual(5, model.Rule.Nodes.Count, "the 3 originals plus the 2 pasted.");

            RuleConnection internalCopy = model.Rule.Connections.FirstOrDefault(c => newIds.Contains(c.FromNode) && newIds.Contains(c.ToNode));
            Assert.IsNotNull(internalCopy, "the internal wire between the two copied nodes should be kept, remapped to the new Ids.");

            Assert.IsFalse(model.Rule.Connections.Any(c => newIds.Contains(c.FromNode) && c.ToNode == "c"), "a wire to a node outside the copy must not appear on the pasted copy.");

            CollectionAssert.AreEquivalent(newIds, model.Canvas.SelectedNodeIds);
        }

        [Test]
        public void CopyPaste_WorksBetweenTwoDifferentRules()
        {
            var ruleA = NewRule("rule.a");
            ruleA.Nodes.Add(Node("x", "block.flow.stop", 0, 0));
            var modelA = new RuleEditorModel(ruleA, BuildContext());
            modelA.Canvas.SelectNodeById("x", additive: false);
            string clipboard = modelA.CopySelectionToClipboardText();

            var ruleB = NewRule("rule.b");
            var modelB = new RuleEditorModel(ruleB, BuildContext());

            IReadOnlyList<string> pasted = modelB.PasteFromClipboardText(clipboard, new Vector2(10, 10));

            Assert.AreEqual(1, pasted.Count);
            Assert.AreEqual(1, modelB.Rule.Nodes.Count);
            Assert.AreEqual("block.flow.stop", modelB.Rule.Nodes[0].BlockTypeId);
        }

        [Test]
        public void Paste_Undo_Redo()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("a", "block.flow.stop", 0, 0));
            var model = new RuleEditorModel(rule, BuildContext());
            model.Canvas.SelectNodeById("a", additive: false);
            string clipboard = model.CopySelectionToClipboardText();

            model.PasteFromClipboardText(clipboard, new Vector2(100, 100));
            Assert.AreEqual(2, model.Rule.Nodes.Count);

            model.Undo();
            Assert.AreEqual(1, model.Rule.Nodes.Count);

            model.Redo();
            Assert.AreEqual(2, model.Rule.Nodes.Count);
        }

        [Test]
        public void DuplicateSelection_CreatesACopyWithNewIds_OffsetFromTheOriginal()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("a", "block.flow.stop", 0, 0));
            var model = new RuleEditorModel(rule, BuildContext());
            model.Canvas.SelectNodeById("a", additive: false);

            IReadOnlyList<string> newIds = model.DuplicateSelection();

            Assert.AreEqual(1, newIds.Count);
            Assert.AreEqual(2, model.Rule.Nodes.Count);
            RuleNode copy = model.Rule.Nodes.First(n => n.Id == newIds[0]);
            Assert.AreNotEqual(0.0, copy.X);
        }

        // --- Stable saving ---

        [Test]
        public void StableSaving_LoadAndSaveAnUnchangedFile_GivesIdenticalBytes()
        {
            var rule = NewRule("rule.stability");
            rule.Nodes.Add(Node("trigger", "block.trigger.on_event", 10, 20, new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("event.herald_line") }));
            rule.Nodes.Add(Node("wait", "block.flow.wait", 200, 50, new Dictionary<string, RuleFieldValueSource>
            {
                ["amount"] = SettingRef("setting.rules.max_fires_per_tick"),
                ["unit"] = Lit("hours")
            }));
            rule.Nodes.Add(Node("log", "block.action.write_log_line", 400, 50, new Dictionary<string, RuleFieldValueSource> { ["text"] = Lit("done") }));
            rule.Connections.Add(Conn("trigger", "then", "wait", "exec"));
            rule.Connections.Add(Conn("wait", "then", "log", "exec"));

            string firstSave = RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { rule });
            List<RuleDefinition> loaded = RuleJsonSerializer.DeserializeFile(firstSave);
            string secondSave = RuleJsonSerializer.SerializeFile(loaded);

            Assert.AreEqual(firstSave, secondSave);
        }

        [Test]
        public void StableSaving_FieldsAreWrittenInAscendingKeyOrder_RegardlessOfInsertionOrder()
        {
            var rule = NewRule();
            var node = Node("n1", "block.value.math", 0, 0, new Dictionary<string, RuleFieldValueSource>
            {
                ["b"] = Lit(2.0),
                ["operator"] = Lit("add"),
                ["a"] = Lit(1.0)
            });
            rule.Nodes.Add(node);

            string json = RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { rule });
            JObject root = JObject.Parse(json);
            var fieldsObj = (JObject)root["items"][0]["nodes"][0]["fields"];
            List<string> keys = fieldsObj.Properties().Select(p => p.Name).ToList();

            CollectionAssert.AreEqual(new[] { "a", "b", "operator" }, keys);
        }

        // --- Rule Id rules ---

        [Test]
        public void RuleIdValidator_RequiresTheRulePrefix()
        {
            Assert.IsFalse(RuleIdValidator.IsWellFormed("foo"));
            Assert.IsFalse(RuleIdValidator.IsWellFormed("rule."));
            Assert.IsTrue(RuleIdValidator.IsWellFormed("rule.foo"));
        }

        [Test]
        public void RuleIdValidator_RejectsADuplicateAcrossLoadedRules()
        {
            string[] others = { "rule.foo", "rule.bar" };
            Assert.IsNotNull(RuleIdValidator.Validate("rule.foo", others));
            Assert.IsNull(RuleIdValidator.Validate("rule.baz", others));
        }

        [Test]
        public void RuleIdValidator_GeneratesAUniqueIdFromAName_AvoidingCollisions()
        {
            Assert.AreEqual("rule.my_rule", RuleIdValidator.GenerateUniqueId("My Rule", Array.Empty<string>()));
            Assert.AreEqual("rule.my_rule_2", RuleIdValidator.GenerateUniqueId("My Rule", new[] { "rule.my_rule" }));
            Assert.AreEqual("rule.my_rule_3", RuleIdValidator.GenerateUniqueId("My Rule", new[] { "rule.my_rule", "rule.my_rule_2" }));
        }

        // --- Validation ---

        [Test]
        public void Validation_ABrokenRuleShowsErrorsOnTheRightNode_FixingItClearsThem()
        {
            var rule = NewRule();
            rule.Nodes.Add(Node("trigger", "block.trigger.on_event", fields: new Dictionary<string, RuleFieldValueSource> { ["event"] = Lit("event.herald_line") }));
            rule.Nodes.Add(Node("ask", "block.flow.ask_player", fields: new Dictionary<string, RuleFieldValueSource> { ["choice1"] = Lit("A") }));
            rule.Connections.Add(Conn("trigger", "then", "ask", "exec"));

            var model = new RuleEditorModel(rule, BuildContext());

            Assert.IsTrue(model.LastValidation.HasErrors);
            CollectionAssert.Contains(model.NodeIdsWithErrors, "ask");

            model.SetFieldValue("ask", "choice2", Lit("B"));

            Assert.IsFalse(model.LastValidation.HasErrors, string.Join("\n", model.LastValidation.Errors.Select(e => e.Message)));
            CollectionAssert.DoesNotContain(model.NodeIdsWithErrors, "ask");
        }

        [Test]
        public void Validation_RunsRegardlessOfTheRulesOwnEnabledFlag()
        {
            var rule = NewRule();
            rule.Enabled = false;
            rule.Nodes.Add(Node("ask", "block.flow.ask_player")); // fewer than two choices filled -> error, even though disabled

            var model = new RuleEditorModel(rule, BuildContext());

            Assert.IsTrue(model.LastValidation.HasErrors, "validation should still catch problems even while the rule itself is disabled.");
        }
    }
}
