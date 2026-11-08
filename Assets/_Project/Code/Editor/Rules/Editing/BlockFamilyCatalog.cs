using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>
    /// Detects block families in a BlockRegistry: any set of 2 or more block
    /// types whose Ids share a prefix and differ only by a ".&lt;type&gt;"
    /// suffix matching a RuleDataType, the same convention CoreBlocks.cs uses
    /// to register one block per data type (Get Local, Set Local, Read Event
    /// Parameter, and any module-registered family built the same way).
    /// </summary>
    public static class BlockFamilyCatalog
    {
        private static readonly RuleDataType[] AllDataTypes = (RuleDataType[])Enum.GetValues(typeof(RuleDataType));

        public static IReadOnlyList<BlockFamily> DetectFamilies(BlockRegistry blocks)
        {
            var groups = new Dictionary<string, Dictionary<RuleDataType, BlockType>>();
            var categoryByKey = new Dictionary<string, string>();
            var displayNameByKey = new Dictionary<string, string>();

            foreach (BlockType blockType in blocks.ListByCategory().SelectMany(g => g))
            {
                foreach (RuleDataType type in AllDataTypes)
                {
                    string suffix = "." + type.ToString().ToLowerInvariant();
                    if (!blockType.Id.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string familyKey = blockType.Id.Substring(0, blockType.Id.Length - suffix.Length);
                    if (!groups.TryGetValue(familyKey, out Dictionary<RuleDataType, BlockType> variants))
                    {
                        variants = new Dictionary<RuleDataType, BlockType>();
                        groups[familyKey] = variants;
                    }

                    variants[type] = blockType;
                    categoryByKey[familyKey] = blockType.Category;
                    displayNameByKey[familyKey] = StripTypeSuffix(blockType.DisplayName);
                    break;
                }
            }

            return groups
                .Where(kvp => kvp.Value.Count > 1)
                .Select(kvp => new BlockFamily(kvp.Key, displayNameByKey[kvp.Key], categoryByKey[kvp.Key], kvp.Value))
                .OrderBy(f => f.FamilyKey, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Finds the family a block type belongs to, if any, by Id (not by object identity, so it works against any BlockRegistry that registers the same Ids).</summary>
        public static bool TryGetFamilyForBlockType(IReadOnlyList<BlockFamily> families, string blockTypeId, out BlockFamily family)
        {
            foreach (BlockFamily candidate in families)
            {
                if (candidate.VariantsByType.Values.Any(v => v.Id == blockTypeId))
                {
                    family = candidate;
                    return true;
                }
            }

            family = null;
            return false;
        }

        private static string StripTypeSuffix(string displayName)
        {
            int parenIndex = displayName.LastIndexOf('(');
            return parenIndex > 0 ? displayName.Substring(0, parenIndex).TrimEnd() : displayName;
        }
    }
}
