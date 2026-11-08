using System;

namespace TheEmptying.Core.Rules
{
    /// <summary>One port on a block type: a named plug, either an execution wire or a typed data value.</summary>
    public sealed class PortDefinition
    {
        public string Name { get; }
        public PortDirection Direction { get; }
        public PortKind Kind { get; }

        /// <summary>Only set for Data ports; null for Execution ports.</summary>
        public RuleDataType? DataType { get; }

        /// <summary>Only meaningful for a Data Input port: whether it must be satisfied by a wire or a field value.</summary>
        public bool Required { get; }

        public PortDefinition(string name, PortDirection direction, PortKind kind, RuleDataType? dataType = null, bool required = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }

            if (kind == PortKind.Data && dataType == null)
            {
                throw new ArgumentException("A data port must declare a RuleDataType.", nameof(dataType));
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
