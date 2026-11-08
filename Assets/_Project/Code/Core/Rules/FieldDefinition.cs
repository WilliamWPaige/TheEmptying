using System;

namespace TheEmptying.Core.Rules
{
    /// <summary>One field on a block type: a value edited directly on the node, either typed in or pointed at a named setting.</summary>
    public sealed class FieldDefinition
    {
        public string Name { get; }
        public RuleDataType DataType { get; }
        public object DefaultValue { get; }
        public bool AllowsSettingReference { get; }

        public FieldDefinition(string name, RuleDataType dataType, object defaultValue, bool allowsSettingReference = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }

            Name = name;
            DataType = dataType;
            DefaultValue = defaultValue;
            AllowsSettingReference = allowsSettingReference;
        }
    }
}
