using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Prompt 2.6b: the add-node menu's content (item-to-block mapping) and effect (adding/connecting), with no AdvancedDropdown involved - the same code path the real menu's callback uses.</summary>
    public class AddNodeMenuModelTests
    {
        private static RuleDefinition NewRule(string id = "rule.test") =>
            new RuleDefinition { Id = id, Name = id, Description = string.Empty, Enabled = true, Nodes = new List<RuleNode>(), Connections = new List<RuleConnection>() };

        private static EditorContext BuildContext()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", "{\"type\":\"module_manifest\",\"items\":[]}");
            return EditorContext.Build(new[] { source.FolderPath }, Array.Empty<IModule>());
        }

        // --- BuildEntries: item-to-block mapping ---

        [Test]
        public void BuildEntries_EveryEntryResolvesToARealBlockType()
        {
            EditorContext context = BuildContext();
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);

            IReadOnlyList<AddNodeMenuEntry> entries = AddNodeMenuModel.BuildEntries(context.Blocks, families);

            Assert.IsNotEmpty(entries);
            foreach (AddNodeMenuEntry entry in entries)
            {
                Assert.IsTrue(context.Blocks.TryGet(entry.BlockType.Id, out BlockType registered), $"entry '{entry.DisplayName}' must resolve to a registered block type.");
                Assert.AreSame(registered, entry.BlockType);
            }
        }

        [Test]
        public void BuildEntries_FamilyMembersAreNotListedIndividually_OnlyTheFamilyEntryAppears()
        {
            EditorContext context = BuildContext();
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);
            BlockFamily getLocal = families.First(f => f.FamilyKey == "block.value.get_local");

            IReadOnlyList<AddNodeMenuEntry> entries = AddNodeMenuModel.BuildEntries(context.Blocks, families);

            foreach (BlockType variant in getLocal.VariantsByType.Values)
            {
                Assert.IsFalse(entries.Any(e => e.Family == null && e.BlockType.Id == variant.Id), $"'{variant.Id}' must not appear as its own plain entry.");
            }

            List<AddNodeMenuEntry> familyEntries = entries.Where(e => e.Family == getLocal).ToList();
            Assert.AreEqual(1, familyEntries.Count, "the family must appear exactly once.");
            Assert.IsTrue(getLocal.VariantsByType.Values.Any(v => v.Id == familyEntries[0].BlockType.Id));
        }

        [Test]
        public void BuildEntries_WithFilter_OnlyIncludesEntriesWithACompatiblePort()
        {
            EditorContext context = BuildContext();
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);

            // Only blocks with an Execution input port (as if dragging from an Execution output).
            Func<BlockType, bool> filter = bt => bt.Ports.Any(p => p.Direction == PortDirection.Input && p.Kind == PortKind.Execution);

            IReadOnlyList<AddNodeMenuEntry> entries = AddNodeMenuModel.BuildEntries(context.Blocks, families, filter);

            Assert.IsNotEmpty(entries);
            foreach (AddNodeMenuEntry entry in entries)
            {
                Assert.IsTrue(entry.BlockType.Ports.Any(p => p.Direction == PortDirection.Input && p.Kind == PortKind.Execution), $"'{entry.DisplayName}' must have passed the filter.");
            }

            Assert.IsFalse(entries.Any(e => e.BlockType.Id == "block.trigger.on_event"), "a trigger (no execution input) must be excluded by this filter.");
        }

        // --- ApplyChoice: same code path the real menu callback uses ---

        [Test]
        public void ApplyChoice_PlainBlock_AddsNodeAtPosition_RebuildsCanvas_AndIsOneUndoStep()
        {
            var model = new RuleEditorModel(NewRule(), BuildContext());
            EditorContext context = model.Context;
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);
            AddNodeMenuEntry entry = AddNodeMenuModel.BuildEntries(context.Blocks, families).First(e => e.BlockType.Id == "block.flow.stop");

            string newId = AddNodeMenuModel.ApplyChoice(model, entry, new Vector2(40f, 60f), null, null);

            Assert.AreEqual(1, model.Rule.Nodes.Count);
            Assert.AreEqual(newId, model.Rule.Nodes[0].Id);
            Assert.AreEqual("block.flow.stop", model.Rule.Nodes[0].BlockTypeId);
            Assert.AreEqual(1, model.Canvas.Nodes.Count, "the canvas view-model must be rebuilt to include the new node.");
            Assert.IsTrue(model.Canvas.Nodes.Any(n => n.Node.Id == newId));
            Assert.IsTrue(model.CanUndo);

            model.Undo();
            Assert.AreEqual(0, model.Rule.Nodes.Count);
            Assert.AreEqual(0, model.Canvas.Nodes.Count, "undo must also be reflected on the canvas.");
        }

        [Test]
        public void ApplyChoice_FamilyEntry_AddsTheDefaultVariant()
        {
            var model = new RuleEditorModel(NewRule(), BuildContext());
            EditorContext context = model.Context;
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);
            BlockFamily getLocal = families.First(f => f.FamilyKey == "block.value.get_local");
            AddNodeMenuEntry entry = AddNodeMenuModel.BuildEntries(context.Blocks, families).First(e => e.Family == getLocal);

            AddNodeMenuModel.ApplyChoice(model, entry, Vector2.zero, null, null);

            Assert.AreEqual(1, model.Rule.Nodes.Count);
            Assert.IsTrue(getLocal.VariantsByType.Values.Any(v => v.Id == model.Rule.Nodes[0].BlockTypeId));
        }

        [Test]
        public void ApplyChoice_WireDragFromOutput_ConnectsTheNewNodesCompatibleInput()
        {
            var rule = NewRule();
            rule.Nodes.Add(new RuleNode { Id = "trigger", BlockTypeId = "block.trigger.on_event", X = 0, Y = 0, Fields = new Dictionary<string, RuleFieldValueSource> { ["event"] = RuleFieldValueSource.FromLiteral(Newtonsoft.Json.Linq.JToken.FromObject("event.herald_line")) } });
            var model = new RuleEditorModel(rule, BuildContext());

            NodeViewModel triggerNode = model.Canvas.Nodes.First(n => n.Node.Id == "trigger");
            PortViewModel execOutput = triggerNode.OutputPorts.First(p => p.Name == "then");

            EditorContext context = model.Context;
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);
            AddNodeMenuEntry entry = AddNodeMenuModel.BuildEntries(context.Blocks, families).First(e => e.BlockType.Id == "block.action.write_log_line");

            string newId = AddNodeMenuModel.ApplyChoice(model, entry, new Vector2(300f, 0f), triggerNode, execOutput);

            Assert.AreEqual(1, model.Rule.Connections.Count);
            RuleConnection connection = model.Rule.Connections[0];
            Assert.AreEqual("trigger", connection.FromNode);
            Assert.AreEqual("then", connection.FromPort);
            Assert.AreEqual(newId, connection.ToNode);
            Assert.AreEqual("exec", connection.ToPort);
        }

        [Test]
        public void ApplyChoice_WireDragCase_AddAndConnectAreOneUndoStep()
        {
            var rule = NewRule();
            rule.Nodes.Add(new RuleNode { Id = "trigger", BlockTypeId = "block.trigger.on_event", X = 0, Y = 0, Fields = new Dictionary<string, RuleFieldValueSource> { ["event"] = RuleFieldValueSource.FromLiteral(Newtonsoft.Json.Linq.JToken.FromObject("event.herald_line")) } });
            var model = new RuleEditorModel(rule, BuildContext());

            NodeViewModel triggerNode = model.Canvas.Nodes.First(n => n.Node.Id == "trigger");
            PortViewModel execOutput = triggerNode.OutputPorts.First(p => p.Name == "then");

            EditorContext context = model.Context;
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);
            AddNodeMenuEntry entry = AddNodeMenuModel.BuildEntries(context.Blocks, families).First(e => e.BlockType.Id == "block.action.write_log_line");

            AddNodeMenuModel.ApplyChoice(model, entry, new Vector2(300f, 0f), triggerNode, execOutput);

            Assert.AreEqual(2, model.Rule.Nodes.Count);
            Assert.AreEqual(1, model.Rule.Connections.Count);

            model.Undo();

            Assert.IsFalse(model.CanUndo, "adding the node and connecting it to the dragged-from port must be a single undo step.");
            Assert.AreEqual(1, model.Rule.Nodes.Count, "undo must remove the added node...");
            Assert.AreEqual(0, model.Rule.Connections.Count, "...and the wire to it, together, in one step.");

            model.Redo();
            Assert.AreEqual(2, model.Rule.Nodes.Count);
            Assert.AreEqual(1, model.Rule.Connections.Count);
        }

        [Test]
        public void ApplyChoice_WireDragFromInput_ConnectsTheNewNodesCompatibleOutput()
        {
            var rule = NewRule();
            rule.Nodes.Add(new RuleNode { Id = "log", BlockTypeId = "block.action.write_log_line", X = 300, Y = 0, Fields = new Dictionary<string, RuleFieldValueSource>() });
            var model = new RuleEditorModel(rule, BuildContext());

            NodeViewModel logNode = model.Canvas.Nodes.First(n => n.Node.Id == "log");
            PortViewModel execInput = logNode.InputPorts.First(p => p.Name == "exec");

            EditorContext context = model.Context;
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(context.Blocks);
            Func<BlockType, bool> filter = bt => bt.Ports.Any(p => p.Direction == PortDirection.Output && p.Kind == PortKind.Execution);
            AddNodeMenuEntry entry = AddNodeMenuModel.BuildEntries(context.Blocks, families, filter).First(e => e.BlockType.Id == "block.trigger.on_event");

            string newId = AddNodeMenuModel.ApplyChoice(model, entry, new Vector2(0f, 0f), logNode, execInput);

            Assert.AreEqual(1, model.Rule.Connections.Count);
            RuleConnection connection = model.Rule.Connections[0];
            Assert.AreEqual(newId, connection.FromNode);
            Assert.AreEqual("then", connection.FromPort);
            Assert.AreEqual("log", connection.ToNode);
            Assert.AreEqual("exec", connection.ToPort);
        }
    }
}
