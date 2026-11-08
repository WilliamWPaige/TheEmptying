using System;

namespace TheEmptying.Core.Rules
{
    /// <summary>One port on a block type: a named plug, either an execution wire or a typed data value.</summary>
    public sealed class PortDefinition
    {
        public string Name { get; }
        public PortDirection Direction { get; }
        public PortKind Kind { get; }

        /// <summary>
        /// Null for an Execution port. For a Data port, either a concrete type
        /// the wire must match, or null for a wildcard port that accepts any
        /// data type (e.g. Post Event's parameter inputs).
        /// </summary>
        public RuleDataType? DataType { get; }

        /// <summary>Only meaningful for a Data Input port: whether it must be satisfied by a wire or a field value.</summary>
        public bool Required { get; }

        public PortDefinition(string name, PortDirection direction, PortKind kind, RuleDataType? dataType = null, bool required = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }

            if (kind == PortKind.Execution && dataType != null)
            {
                throw new ArgumentException("An execution port must not declare a RuleDataType.", nameof(dataType));
            }

            Name = name;
            Direction = direction;
            Kind = kind;
            DataType = dataType;
            Required = required;
        }
    }
}
