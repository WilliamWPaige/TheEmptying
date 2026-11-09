using System;
using System.Collections.Generic;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Editing;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas.UI
{
    /// <summary>
    /// The add-node menu: every block type grouped by category and
    /// searchable (AdvancedDropdown gives us both for free, without pulling
    /// in Unity's Graph Toolkit). A family (Get Local, Set Local, Read Event
    /// Parameter, ...) appears once; picking it adds the default (Number)
    /// variant, which the node's own type dropdown can then swap.
    ///
    /// The entry list comes from AddNodeMenuModel (plain, EditMode-tested)
    /// and each dropdown item carries its entry directly (EntryDropdownItem),
    /// so selection never goes through an id-to-block lookup table that
    /// could go stale if AdvancedDropdown ever rebuilds its tree more than
    /// once for the same Show() call.
    /// </summary>
    internal sealed class AddNodeMenu : AdvancedDropdown
    {
        public event Action<AddNodeMenuEntry> EntryChosen;

        private sealed class EntryDropdownItem : AdvancedDropdownItem
        {
            public AddNodeMenuEntry Entry { get; }

            public EntryDropdownItem(AddNodeMenuEntry entry) : base(entry.DisplayName)
            {
                Entry = entry;
            }
        }

        private readonly IReadOnlyList<AddNodeMenuEntry> _entries;

        public AddNodeMenu(BlockRegistry blocks, IReadOnlyList<BlockFamily> families, Func<BlockType, bool> filter = null)
            : base(new AdvancedDropdownState())
        {
            _entries = AddNodeMenuModel.BuildEntries(blocks, families, filter);
            minimumSize = new Vector2(260f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Add Node");
            var byCategory = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);

            foreach (AddNodeMenuEntry entry in _entries)
            {
                AdvancedDropdownItem categoryItem = GetOrCreateCategory(root, byCategory, entry.Category);
                categoryItem.AddChild(new EntryDropdownItem(entry));
            }

            return root;
        }

        private static AdvancedDropdownItem GetOrCreateCategory(AdvancedDropdownItem root, Dictionary<string, AdvancedDropdownItem> byCategory, string category)
        {
            if (byCategory.TryGetValue(category, out AdvancedDropdownItem item))
            {
                return item;
            }

            item = new AdvancedDropdownItem(category);
            byCategory[category] = item;
            root.AddChild(item);
            return item;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is EntryDropdownItem entryItem)
            {
                EntryChosen?.Invoke(entryItem.Entry);
            }
        }
    }
}
