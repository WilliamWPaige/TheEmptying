using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Events
{
    public sealed class EventDefinitionRegistry
    {
        private readonly Dictionary<string, EventDefinition> _definitions = new Dictionary<string, EventDefinition>();

        public void Register(EventDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (_definitions.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException($"An event definition with Id '{definition.Id}' is already registered.");
            }

            _definitions[definition.Id] = definition;
        }

        public bool IsRegistered(string definitionId)
        {
            return definitionId != null && _definitions.ContainsKey(definitionId);
        }

        public bool TryGet(string definitionId, out EventDefinition definition)
        {
            return _definitions.TryGetValue(definitionId, out definition);
        }

        public EventDefinition Get(string definitionId)
        {
            if (!_definitions.TryGetValue(definitionId, out EventDefinition definition))
            {
                throw new KeyNotFoundException($"No event definition registered for Id '{definitionId}'.");
            }

            return definition;
        }
    }
}
