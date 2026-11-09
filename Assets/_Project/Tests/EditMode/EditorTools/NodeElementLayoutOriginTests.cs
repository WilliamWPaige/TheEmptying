using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules;
using TheEmptying.Editor.Rules.Canvas;
using TheEmptying.Editor.Rules.Canvas.UI;
using TheEmptying.Editor.Rules.Editing;
using TheEmptying.Tests.EditMode.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>
    /// Prompt 2.6c: NodeElement's port labels and field rows were landing one
    /// HeaderHeight too low, and field rows spilled past the node's bottom
    /// edge, even though NodeViewModel's layout math (covered by
    /// NodeViewModelLayoutTests) was already correct. The cause was that the
    /// container holding those labels/fields (_portsAndFields) was a normal
    /// flow sibling placed BELOW the header by the header's own height, while
    /// NodeViewModel's Y values already assume an origin at the node's own
    /// top-left (the same origin the port dots use, since dots are added
    /// directly to the node). So every label/field ended up offset by
    /// HeaderHeight twice.
    ///
    /// Forcing a real Yoga layout pass in a batch-mode EditMode test isn't
    /// reliably available (NodeElement is never attached to a shown,
    /// updating panel here), so this test takes the sanctioned fallback:
    /// it checks structurally that the container sharing every label/field's
    /// position shares the same (0,0) origin as the node itself - the exact
    /// thing that was wrong, and the exact thing the fix changes. Combined
    /// with NodeViewModelLayoutTests (which proves the Y values themselves
    /// are correct relative to that origin), this proves the final on-screen
    /// position is correct without needing a live panel.
    /// </summary>
    public class NodeElementLayoutOriginTests
    {
        private static RuleEditorModel BuildModelWithOneNodeOfEachCoreBlock(out List<string> blockTypeIds)
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", "{\"type\":\"module_manifest\",\"items\":[]}");
            EditorContext context = EditorContext.Build(new[] { source.FolderPath }, Array.Empty<IModule>());

            var rule = new RuleDefinition { Id = "rule.test", Name = "test", Description = string.Empty, Enabled = true, Nodes = new List<RuleNode>(), Connections = new List<RuleConnection>() };
            blockTypeIds = new List<string>();

            int n = 0;
            foreach (var group in context.Blocks.ListByCategory())
            {
                foreach (BlockType blockType in group)
                {
                    string id = $"n{n++}";
                    rule.Nodes.Add(new RuleNode { Id = id, BlockTypeId = blockType.Id, X = 0, Y = 0, Fields = new Dictionary<string, RuleFieldValueSource>() });
                    blockTypeIds.Add(blockType.Id);
                }
            }

            return new RuleEditorModel(rule, context);
        }

        [Test]
        public void PortsAndFieldsContainer_SharesTheNodesOwnOrigin_ForEveryCoreBlock()
        {
            RuleEditorModel model = BuildModelWithOneNodeOfEachCoreBlock(out List<string> blockTypeIds);

            Assert.AreEqual(blockTypeIds.Count, model.Canvas.Nodes.Count);

            foreach (NodeViewModel node in model.Canvas.Nodes)
            {
                var element = new NodeElement(node, model);
                VisualElement container = element.PortsAndFieldsContainerForTests;

                Assert.AreEqual(Position.Absolute, container.style.position.value,
                    $"'{node.Node.BlockTypeId}': the ports/fields container must be absolutely positioned, not a normal-flow sibling of the header (which would push it down by HeaderHeight).");
                Assert.AreEqual(0f, container.style.left.value.value,
                    $"'{node.Node.BlockTypeId}': the ports/fields container's left must be 0 - the same origin the port dots (direct children of the node) use.");
                Assert.AreEqual(0f, container.style.top.value.value,
                    $"'{node.Node.BlockTypeId}': the ports/fields container's top must be 0 - the same origin the port dots (direct children of the node) use.");
            }
        }

        [Test]
        public void Header_IsTheOnlyNormalFlowChild_SoNothingElseCanBePushedDownBelowIt()
        {
            RuleEditorModel model = BuildModelWithOneNodeOfEachCoreBlock(out List<string> blockTypeIds);
            NodeViewModel first = model.Canvas.Nodes[0];
            var element = new NodeElement(first, model);

            int inFlowChildren = 0;
            foreach (VisualElement child in element.Children())
            {
                if (child.style.position.value != Position.Absolute)
                {
                    inFlowChildren++;
                }
            }

            Assert.AreEqual(1, inFlowChildren, "only the header should be a normal-flow child of the node; everything else must be absolutely positioned at the node's own origin.");
        }
    }
}
