using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// Describes one kind of node a rule can use: its Id, category, kind, ports
    /// and fields. Runtime behavior (what the block actually does) is added by
    /// prompt 2.2; this is metadata only.
    /// </summary>
    public abstract class BlockType
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Category { get; }
        public abstract BlockKind Kind { get; }
        public abstract IReadOnlyList<PortDefinition> Ports { get; }
        public abstract IReadOnlyList<FieldDefinition> Fields { get; }

        public PortDefinition FindPort(string name, PortDirection direction)
        {
            return Ports.FirstOrDefault(p => p.Direction == direction && p.Name == name);
        }

        public FieldDefinition FindField(string name)
        {
            return Fields.FirstOrDefault(f => f.Name == name);
        }

        /// <summary>
        /// For a Trigger block: which event definition Id this node's fields
        /// make it listen to, or null if it isn't event-triggered. Used only by
        /// the compiler's loop warning.
        /// </summary>
        public virtual string GetTriggerEventDefinitionId(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            return null;
        }

        /// <summary>
        /// For an Action block: which event definition Ids this node's fields
        /// make it post. Used only by the compiler's loop warning.
        /// </summary>
        public virtual IEnumerable<string> GetPostedEventDefinitionIds(IReadOnlyDictionary<string, RuleFieldValueSource> fieldValues)
        {
            return Enumerable.Empty<string>();
        }
    }
}
