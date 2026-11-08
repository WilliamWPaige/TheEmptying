using System.Collections.Generic;
using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>
    /// A group of block types that are really "the same block" with one
    /// variant per data type (Get Local, Set Local, Read Event Parameter, and
    /// any others registered the same way). The add-node menu shows one entry
    /// for the family; a node built from it shows a type dropdown that swaps
    /// between the variants in VariantsByType.
    /// </summary>
    public sealed class BlockFamily
    {
        /// <summary>The shared Id prefix before the ".&lt;type&gt;" suffix (e.g. "block.value.get_local").</summary>
        public string FamilyKey { get; }

        public string DisplayName { get; }
        public string Category { get; }
        public IReadOnlyDictionary<RuleDataType, BlockType> VariantsByType { get; }

        public BlockFamily(string familyKey, string displayName, string category, IReadOnlyDictionary<RuleDataType, BlockType> variantsByType)
        {
            FamilyKey = familyKey;
            DisplayName = displayName;
            Category = category;
            VariantsByType = variantsByType;
        }

        public bool TryGetVariant(RuleDataType type, out BlockType blockType) => VariantsByType.TryGetValue(type, out blockType);
    }
}
