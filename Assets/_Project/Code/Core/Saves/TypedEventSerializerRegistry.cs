using System.Collections.Generic;

namespace TheEmptying.Core.Saves
{
    /// <summary>
    /// Serializers for typed events, keyed by definition Id. Posting a typed
    /// event with no serializer is fine at runtime; saving one is an error.
    /// </summary>
    public sealed class TypedEventSerializerRegistry
    {
        private readonly Dictionary<string, ITypedEventSerializer> _serializers = new Dictionary<string, ITypedEventSerializer>();

        public void Register(string definitionId, ITypedEventSerializer serializer)
        {
            _serializers[definitionId] = serializer;
        }

        public bool TryGet(string definitionId, out ITypedEventSerializer serializer)
        {
            return _serializers.TryGetValue(definitionId, out serializer);
        }
    }
}
