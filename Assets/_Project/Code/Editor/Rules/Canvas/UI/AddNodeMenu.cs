using System;
using System.Collections.Generic;
using System.Linq;
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
    /// </summary>
    internal sealed class AddNodeMenu : AdvancedDropdown
    {
        public event Action<BlockType> BlockTypeChosen;

        private readonly Dictionary<int, BlockType> _blockTypesById = new Dictionary<int, BlockType>();
        private int _nextId = 1;

        private readonly BlockRegistry _blocks;
        private readonly IReadOnlyList<BlockFamily> _families;
        private readonly Func<BlockType, bool> _filter;

        public AddNodeMenu(BlockRegistry blocks, IReadOnlyList<BlockFamily> families, Func<BlockType, bool> filter = null)
            : base(new AdvancedDropdownState())
        {
            _blocks = blocks;
            _families = families;
            _filter = filter;
            minimumSize = new Vector2(260f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Add Node");
            var byCategory = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);

            var familyBlockTypeIds = new HashSet<string>(_families.SelectMany(f => f.VariantsByType.Values.Select(v => v.Id)));

            foreach (IGrouping<string, BlockType> group in _blocks.ListByCategory())
            {
                foreach (BlockType blockType in group)
                {
                    if (familyBlockTypeIds.Contains(blockType.Id))
                    {
                        continue; // represented by its family entry instead
                    }

                    if (_filter != null && !_filter(blockType))
                    {
                        continue;
                    }

                    AdvancedDropdownItem categoryItem = GetOrCreateCategory(root, byCategory, blockType.Category);
                    var item = new AdvancedDropdownItem(blockType.DisplayName) { id = _nextId };
                    _blockTypesById[_nextId] = blockType;
                    _nextId++;
                    categoryItem.AddChild(item);
                }
            }

            foreach (BlockFamily family in _families)
            {
                BlockType defaultVariant = family.VariantsByType.Values.FirstOrDefault(v => _filter == null || _filter(v));
                if (defaultVariant == null)
                {
                    continue;
                }

                AdvancedDropdownItem categoryItem = GetOrCreateCategory(root, byCategory, family.Category);
                var item = new AdvancedDropdownItem(family.DisplayName) { id = _nextId };
                _blockTypesById[_nextId] = defaultVariant;
                _nextId++;
                categoryItem.AddChild(item);
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
            if (_blockTypesById.TryGetValue(item.id, out BlockType blockType))
            {
                BlockTypeChosen?.Invoke(blockType);
            }
        }
    }
}
