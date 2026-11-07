using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// Which content types exist and how to load them. The core registers the
    /// built-in types; modules register their own during the declaration phase.
    /// </summary>
    public sealed class ContentTypeRegistry
    {
        private readonly Dictionary<string, ContentTypeDefinition> _types = new Dictionary<string, ContentTypeDefinition>();

        public void Register(ContentTypeDefinition type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (_types.ContainsKey(type.TypeName))
            {
                throw new InvalidOperationException($"Content type '{type.TypeName}' is already registered.");
            }

            _types[type.TypeName] = type;
        }

        public bool TryGet(string typeName, out ContentTypeDefinition type)
        {
            return _types.TryGetValue(typeName, out type);
        }

        public bool TryGetTypeNameForClrType(Type clrType, out string typeName)
        {
            foreach (KeyValuePair<string, ContentTypeDefinition> kvp in _types)
            {
                if (kvp.Value.ClrType == clrType)
                {
                    typeName = kvp.Key;
                    return true;
                }
            }

            typeName = null;
            return false;
        }

        public IReadOnlyCollection<string> TypeNames => _types.Keys;
    }
}
