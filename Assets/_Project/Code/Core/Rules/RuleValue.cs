using System;
using System.Globalization;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Rules
{
    /// <summary>A small tagged value holding exactly one RuleDataType's worth of data.</summary>
    public readonly struct RuleValue
    {
        public RuleDataType DataType { get; }

        private readonly double _number;
        private readonly bool _bool;
        private readonly string _text;
        private readonly EntityId _entity;

        private RuleValue(RuleDataType dataType, double number, bool boolean, string text, EntityId entity)
        {
            DataType = dataType;
            _number = number;
            _bool = boolean;
            _text = text;
            _entity = entity;
        }

        public static RuleValue Number(double value) => new RuleValue(RuleDataType.Number, value, false, null, default);
        public static RuleValue Bool(bool value) => new RuleValue(RuleDataType.Bool, 0, value, null, default);
        public static RuleValue Text(string value) => new RuleValue(RuleDataType.Text, 0, false, value ?? string.Empty, default);
        public static RuleValue ContentId(string value) => new RuleValue(RuleDataType.ContentId, 0, false, value ?? string.Empty, default);
        public static RuleValue Entity(EntityId value) => new RuleValue(RuleDataType.Entity, 0, false, null, value);

        public double AsNumber => DataType == RuleDataType.Number ? _number : throw WrongType(RuleDataType.Number);
        public bool AsBool => DataType == RuleDataType.Bool ? _bool : throw WrongType(RuleDataType.Bool);
        public string AsText => DataType == RuleDataType.Text || DataType == RuleDataType.ContentId ? (_text ?? string.Empty) : throw WrongType(DataType);
        public EntityId AsEntity => DataType == RuleDataType.Entity ? _entity : throw WrongType(RuleDataType.Entity);

        public static RuleValue Default(RuleDataType type)
        {
            return type switch
            {
                RuleDataType.Number => Number(0),
                RuleDataType.Bool => Bool(false),
                RuleDataType.Text => Text(string.Empty),
                RuleDataType.ContentId => ContentId(string.Empty),
                RuleDataType.Entity => Entity(EntityId.None),
                _ => Number(0)
            };
        }

        /// <summary>Converts a compiled literal (as stored by the compiler) into a RuleValue of the declared type.</summary>
        public static RuleValue FromLiteral(RuleDataType type, object literal)
        {
            switch (type)
            {
                case RuleDataType.Number:
                    return Number(literal == null ? 0.0 : Convert.ToDouble(literal, CultureInfo.InvariantCulture));
                case RuleDataType.Bool:
                    return Bool(literal is bool b && b);
                case RuleDataType.Text:
                    return Text(literal as string ?? string.Empty);
                case RuleDataType.ContentId:
                    return ContentId(literal as string ?? string.Empty);
                case RuleDataType.Entity:
                    return EntityId.TryParse(literal as string, out EntityId id) ? Entity(id) : Entity(EntityId.None);
                default:
                    return Default(type);
            }
        }

        public override string ToString()
        {
            return DataType switch
            {
                RuleDataType.Number => _number.ToString(CultureInfo.InvariantCulture),
                RuleDataType.Bool => _bool ? "true" : "false",
                RuleDataType.Text => _text ?? string.Empty,
                RuleDataType.ContentId => _text ?? string.Empty,
                RuleDataType.Entity => _entity.ToString(),
                _ => string.Empty
            };
        }

        private InvalidOperationException WrongType(RuleDataType expected)
        {
            return new InvalidOperationException($"RuleValue holds {DataType}, not {expected}.");
        }
    }
}
