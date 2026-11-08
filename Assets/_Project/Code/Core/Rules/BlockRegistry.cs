using System;
using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Core.Rules
{
    /// <summary>Every block type, by Id. Modules register their block types during the declaration phase; the core registers its own too.</summary>
    public sealed class BlockRegistry
    {
        private readonly Dictionary<string, BlockType> _types = new Dictionary<string, BlockType>();

        public void Register(BlockType blockType)
        {
            if (blockType == null)
            {
                throw new ArgumentNullException(nameof(blockType));
            }

            if (_types.ContainsKey(blockType.Id))
            {
                throw new InvalidOperationException($"Block type '{blockType.Id}' is already registered.");
            }

            _types[blockType.Id] = blockType;
        }

        public bool TryGet(string id, out BlockType blockType)
        {
            return _types.TryGetValue(id, out blockType);
        }

        public BlockType Get(string id)
        {
            if (!_types.TryGetValue(id, out BlockType blockType))
            {
                throw new KeyNotFoundException($"No block type registered for Id '{id}'.");
            }

            return blockType;
        }

        /// <summary>Every registered block type, in ascending category then Id order (ordinal), grouped by category.</summary>
        public IEnumerable<IGrouping<string, BlockType>> ListByCategory()
        {
            return _types.Values
                .OrderBy(t => t.Category, StringComparer.Ordinal)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .GroupBy(t => t.Category);
        }
    }
}
