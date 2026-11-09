using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
using TheEmptying.Editor.Rules.Canvas;
using TheEmptying.Editor.Rules.Editing;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>
    /// Prompt 2.6b: every node-drawing position (port label rows, node
    /// height, the field-name label column width) is computed by
    /// NodeViewModel, not NodeElement, so it's covered here without a live
    /// window - this is what makes bugs 2, 3 and 4 regression-testable.
    /// </summary>
    public class NodeViewModelLayoutTests
    {
        private static BlockRegistry Blocks()
        {
            var registry = new BlockRegistry();
            CoreBlocks.RegisterAll(registry);
            return registry;
        }

        private static RuleNode Node(string id, string blockType) =>
            new RuleNode { Id = id, BlockTypeId = blockType, X = 0, Y = 0, Fields = new Dictionary<string, RuleFieldValueSource>() };

        private static NodeViewModel BuildNode(BlockRegistry blocks, string blockTypeId)
        {
            Assert.IsTrue(blocks.TryGet(blockTypeId, out BlockType blockType), $"'{blockTypeId}' must be registered.");
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(blocks);
            BlockFamilyCatalog.TryGetFamilyForBlockType(families, blockTypeId, out BlockFamily family);
            float labelColumnWidth = NodeViewModel.ComputeLabelColumnWidth(blocks);
            return new NodeViewModel(Node("n1", blockTypeId), blockType, null, family, labelColumnWidth);
        }

        // --- Port labels must be level with their own dot ---

        [Test]
        public void PortLabelTop_IsCenteredOnThePortsOwnRow_ForEveryCoreBlock()
        {
            BlockRegistry blocks = Blocks();

            foreach (IGrouping<string, BlockType> group in blocks.ListByCategory())
            {
                foreach (BlockType blockType in group)
                {
                    NodeViewModel node = BuildNode(blocks, blockType.Id);

                    foreach (PortViewModel port in node.Ports)
                    {
                        float labelCenter = NodeViewModel.PortLabelTop(port) + NodeViewModel.PortLabelHeight * 0.5f;
                        Assert.That(labelCenter, Is.EqualTo(port.LocalOffset.y).Within(0.001f),
                            $"'{blockType.Id}' port '{port.Name}': label must be centered on the dot's own row.");
                    }
                }
            }
        }

        [Test]
        public void PortLabelTop_DiffersAcrossRows_SoDifferentPortsAreNeverOnTopOfEachOther()
        {
            BlockRegistry blocks = Blocks();
            NodeViewModel node = BuildNode(blocks, "block.flow.ask_player"); // exec + arg0 + arg1 + arg2 inputs, several rows

            List<float> tops = node.InputPorts.Select(NodeViewModel.PortLabelTop).ToList();
            Assert.AreEqual(tops.Count, tops.Distinct().Count(), "every input port on a multi-row node must sit on a distinct row.");
        }

        // --- Node height must cover every port row, field row and the type dropdown ---

        [Test]
        public void Size_CoversEveryPortRowAndFieldRow_ForEveryCoreBlock()
        {
            BlockRegistry blocks = Blocks();

            foreach (IGrouping<string, BlockType> group in blocks.ListByCategory())
            {
                foreach (BlockType blockType in group)
                {
                    NodeViewModel node = BuildNode(blocks, blockType.Id);

                    int rowCount = System.Math.Max(node.InputPorts.Count, node.OutputPorts.Count);
                    float expectedContentTop = NodeViewModel.HeaderHeight + NodeViewModel.PortsTopPadding + rowCount * NodeViewModel.PortRowHeight;
                    if (node.HasVariantDropdown)
                    {
                        expectedContentTop += NodeViewModel.FieldRowHeight + NodeViewModel.FieldRowSpacing;
                    }

                    float expectedHeight = expectedContentTop + blockType.Fields.Count * (NodeViewModel.FieldRowHeight + NodeViewModel.FieldRowSpacing) + NodeViewModel.BottomPadding;

                    Assert.That(node.Size.y, Is.EqualTo(expectedHeight).Within(0.001f), $"'{blockType.Id}': node height must cover every port row, field row and the type dropdown.");

                    if (blockType.Fields.Count > 0)
                    {
                        float lastFieldBottom = node.FieldRowY[^1] + NodeViewModel.FieldRowHeight;
                        Assert.LessOrEqual(lastFieldBottom, node.Size.y, $"'{blockType.Id}': the last field row must fit inside the node.");
                    }
                }
            }
        }

        [Test]
        public void Size_AccountsForTheFamilyVariantDropdownRow()
        {
            BlockRegistry blocks = Blocks();
            IReadOnlyList<BlockFamily> families = BlockFamilyCatalog.DetectFamilies(blocks);
            BlockFamily getLocal = families.First(f => f.FamilyKey == "block.value.get_local");
            string numberVariantId = getLocal.VariantsByType[RuleDataType.Number].Id;

            NodeViewModel withFamily = BuildNode(blocks, numberVariantId);
            Assert.IsTrue(withFamily.HasVariantDropdown);

            // Build the same block type again but tell it there's no family, to isolate the dropdown row's contribution.
            float labelColumnWidth = NodeViewModel.ComputeLabelColumnWidth(blocks);
            blocks.TryGet(numberVariantId, out BlockType blockType);
            var withoutFamily = new NodeViewModel(Node("n2", numberVariantId), blockType, null, null, labelColumnWidth);

            Assert.That(withFamily.Size.y, Is.EqualTo(withoutFamily.Size.y + NodeViewModel.FieldRowHeight + NodeViewModel.FieldRowSpacing).Within(0.001f));
        }

        // --- The label column must fit the longest field name on any core block ---

        [Test]
        public void ComputeLabelColumnWidth_FitsTheLongestFieldName()
        {
            BlockRegistry blocks = Blocks();
            int longest = blocks.ListByCategory().SelectMany(g => g).SelectMany(bt => bt.Fields).Max(f => f.Name.Length);

            // "defaultChoice" (Ask Player) is the longest at 13 characters as of this writing.
            Assert.GreaterOrEqual(longest, 13);

            float width = NodeViewModel.ComputeLabelColumnWidth(blocks);
            Assert.GreaterOrEqual(width, longest * 5.5f, "the column must be at least wide enough for the longest field name's estimated text width.");
            Assert.LessOrEqual(width, 110f, "one long name must not make the label column absurdly wide.");
        }

        [Test]
        public void ComputeLabelColumnWidth_IsTheSameForEveryNode_SoColumnsLineUp()
        {
            BlockRegistry blocks = Blocks();
            NodeViewModel math = BuildNode(blocks, "block.value.math");
            NodeViewModel log = BuildNode(blocks, "block.action.write_log_line");

            Assert.AreEqual(math.LabelColumnWidth, log.LabelColumnWidth);
        }
    }
}
