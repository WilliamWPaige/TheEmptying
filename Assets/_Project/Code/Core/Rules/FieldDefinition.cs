using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules
{
    /// <summary>One field on a block type: a value edited directly on the node, either typed in or pointed at a named setting.</summary>
    public sealed class FieldDefinition
    {
        public string Name { get; }
        public RuleDataType DataType { get; }
        public object DefaultValue { get; }
        public bool AllowsSettingReference { get; }

        /// <summary>Editor hint: a closed set of values to offer as a dropdown (e.g. an operator or a unit). Null or empty means no restriction - metadata only, the compiler does not enforce it.</summary>
        public IReadOnlyList<string> AllowedValues { get; }

        /// <summary>Editor hint: what kind of searchable picker to offer for this field's value, beyond AllowedValues.</summary>
        public FieldPickerKind PickerKind { get; }

        /// <summary>Only meaningful when PickerKind is ContentId: which content type's Ids to offer.</summary>
        public string ContentTypeName { get; }

        public FieldDefinition(
            string name,
            RuleDataType dataType,
            object defaultValue,
            bool allowsSettingReference = false,
            IReadOnlyList<string> allowedValues = null,
            FieldPickerKind pickerKind = FieldPickerKind.None,
            string contentTypeName = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }

            Name = name;
            DataType = dataType;
            DefaultValue = defaultValue;
            AllowsSettingReference = allowsSettingReference;
            AllowedValues = allowedValues;
            PickerKind = pickerKind;
            ContentTypeName = contentTypeName;
        }
    }
}
