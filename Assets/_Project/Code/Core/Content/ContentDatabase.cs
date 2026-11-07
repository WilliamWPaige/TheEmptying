using System;
using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Core.Content
{
    /// <summary>Stores every loaded definition by type and Id. A later Set with the same type+Id replaces the earlier one and records the override.</summary>
    public sealed class ContentDatabase
    {
        private readonly Dictionary<string, Dictionary<string, ContentEntry>> _byType = new Dictionary<string, Dictionary<string, ContentEntry>>();
        private readonly List<ContentOverride> _overrides = new List<ContentOverride>();

        public IReadOnlyList<ContentOverride> Overrides => _overrides;

        public void Set(string typeName, ContentDefinition definition, string sourceId, string sourceFile)
        {
            if (!_byType.TryGetValue(typeName, out Dictionary<string, ContentEntry> byId))
            {
                byId = new Dictionary<string, ContentEntry>();
                _byType[typeName] = byId;
            }

            if (byId.TryGetValue(definition.Id, out ContentEntry existing))
            {
                _overrides.Add(new ContentOverride(typeName, definition.Id, existing.SourceId, sourceId));
            }

            byId[definition.Id] = new ContentEntry(definition, sourceId, sourceFile);
        }

        public bool TryGet(string typeName, string id, out ContentEntry entry)
        {
            if (_byType.TryGetValue(typeName, out Dictionary<string, ContentEntry> byId))
            {
                return byId.TryGetValue(id, out entry);
            }

            entry = null;
            return false;
        }

        public ContentEntry Get(string typeName, string id)
        {
            if (!TryGet(typeName, id, out ContentEntry entry))
            {
                throw new KeyNotFoundException($"No content of type '{typeName}' with Id '{id}'.");
            }

            return entry;
        }

        public bool HasType(string typeName) => _byType.ContainsKey(typeName);

        /// <summary>Entries of a type, in ascending Id order.</summary>
        public IEnumerable<ContentEntry> ListByType(string typeName)
        {
            if (!_byType.TryGetValue(typeName, out Dictionary<string, ContentEntry> byId))
            {
                return Enumerable.Empty<ContentEntry>();
            }

            return byId.Values.OrderBy(e => e.Definition.Id, StringComparer.Ordinal);
        }
    }
}
