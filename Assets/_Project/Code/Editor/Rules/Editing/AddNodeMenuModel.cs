using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Canvas;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>
    /// One entry in the add-node menu: a plain block, or (for a family like
    /// Get Local) the family and the default variant to add before the user
    /// picks a different type from the node's own dropdown.
    /// </summary>
    public sealed class AddNodeMenuEntry
    {
        public string Category { get; }
        public string DisplayName { get; }
        public BlockType BlockType { get; }
        public BlockFamily Family { get; }

        public AddNodeMenuEntry(string category, string displayName, BlockType blockType, BlockFamily family)
        {
            Category = category;
            DisplayName = displayName;
            BlockType = blockType;
            Family = family;
        }
    }

    /// <summary>
    /// The add-node menu's content and effect, with no AdvancedDropdown or
    /// other UI mechanics - so the item-to-block mapping and "choose this
    /// entry" behavior are both covered by EditMode tests. AddNodeMenu (the
    /// UI) only turns BuildEntries' result into dropdown items and calls
    /// ApplyChoice from the same callback a test calls directly.
    /// </summary>
    public static class AddNodeMenuModel
    {
        /// <summary>Every selectable entry: every block type not already represented by a family, plus one entry per family (its default, or first filter-passing, variant). Grouped by category by the caller via each entry's Category.</summary>
        public static IReadOnlyList<AddNodeMenuEntry> BuildEntries(BlockRegistry blocks, IReadOnlyList<BlockFamily> families, Func<BlockType, bool> filter = null)
        {
            var entries = new List<AddNodeMenuEntry>();
            var familyBlockTypeIds = new HashSet<string>(families.SelectMany(f => f.VariantsByType.Values.Select(v => v.Id)));

            foreach (IGrouping<string, BlockType> group in blocks.ListByCategory())
            {
                foreach (BlockType blockType in group)
                {
                    if (familyBlockTypeIds.Contains(blockType.Id))
                    {
                        continue; // represented by its family entry instead
                    }

                    if (filter != null && !filter(blockType))
                    {
                        continue;
                    }

                    entries.Add(new AddNodeMenuEntry(blockType.Category, blockType.DisplayName, blockType, null));
                }
            }

            foreach (BlockFamily family in families)
            {
                BlockType defaultVariant = family.VariantsByType.Values.FirstOrDefault(v => filter == null || filter(v));
                if (defaultVariant == null)
                {
                    continue;
                }

                entries.Add(new AddNodeMenuEntry(family.Category, family.DisplayName, defaultVariant, family));
            }

            return entries;
        }

        /// <summary>
        /// Adds entry's block type to model at canvasPosition. If
        /// dragSourcePort isn't null, also connects the new node to it on a
        /// compatible port, in whichever direction fits - the add and the
        /// connect are one undo step together, since from the user's point
        /// of view dragging a wire to empty canvas and picking a block is
        /// one action. This is the exact code path the real add-node menu's
        /// callback uses, so a test exercising it is exercising the real
        /// behavior.
        /// </summary>
        public static string ApplyChoice(RuleEditorModel model, AddNodeMenuEntry entry, Vector2 canvasPosition, NodeViewModel dragSourceNode, PortViewModel dragSourcePort)
        {
            if (dragSourcePort == null || dragSourceNode == null)
            {
                return model.AddNode(entry.BlockType.Id, canvasPosition);
            }

            PortDefinition compatiblePort = entry.BlockType.Ports.FirstOrDefault(p =>
                p.Direction != dragSourcePort.Direction &&
                p.Kind == dragSourcePort.Kind &&
                (dragSourcePort.DataType == null || p.DataType == null || p.DataType == dragSourcePort.DataType));

            if (compatiblePort == null)
            {
                return model.AddNode(entry.BlockType.Id, canvasPosition);
            }

            return model.AddNodeAndConnect(entry.BlockType.Id, canvasPosition, dragSourceNode.Node.Id, dragSourcePort.Name, dragSourcePort.Direction, compatiblePort.Name);
        }
    }
}
