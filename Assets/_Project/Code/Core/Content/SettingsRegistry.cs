using System;
using System.Collections.Generic;
using System.Linq;

namespace TheEmptying.Core.Content
{
    /// <summary>
    /// Holds current setting values. Modules declare settings during the
    /// declaration phase; settings loaded from data are declared too.
    /// </summary>
    public sealed class SettingsRegistry
    {
        private readonly Dictionary<string, SettingDefinition> _definitions = new Dictionary<string, SettingDefinition>();
        private readonly Dictionary<string, double> _values = new Dictionary<string, double>();

        public readonly struct SetResult
        {
            public double Value { get; }
            public bool WasClamped { get; }

            public SetResult(double value, bool wasClamped)
            {
                Value = value;
                WasClamped = wasClamped;
            }
        }

        public void Declare(SettingDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (_definitions.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException($"Setting '{definition.Id}' is already declared.");
            }

            _definitions[definition.Id] = definition;
            _values[definition.Id] = definition.Default;
        }

        public bool IsDeclared(string id) => _definitions.ContainsKey(id);

        public bool TryGetDefinition(string id, out SettingDefinition definition)
        {
            return _definitions.TryGetValue(id, out definition);
        }

        public double Get(string id)
        {
            if (!_values.TryGetValue(id, out double value))
            {
                throw new KeyNotFoundException($"Setting '{id}' is not declared.");
            }

            return value;
        }

        public SetResult Set(string id, double value)
        {
            if (!_definitions.TryGetValue(id, out SettingDefinition definition))
            {
                throw new KeyNotFoundException($"Setting '{id}' is not declared.");
            }

            double adjusted = value;

            switch (definition.ValueType)
            {
                case SettingValueType.Integer:
                    adjusted = Math.Round(value, MidpointRounding.AwayFromZero);
                    break;
                case SettingValueType.Boolean:
                    if (value != 0 && value != 1)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), $"Boolean setting '{id}' only accepts 0 or 1 (got {value}).");
                    }

                    break;
            }

            double clamped = Math.Min(Math.Max(adjusted, definition.Minimum), definition.Maximum);
            bool wasClamped = clamped != adjusted;
            _values[id] = clamped;
            return new SetResult(clamped, wasClamped);
        }

        /// <summary>Every setting with its range and category, in ascending Id order. Feeds the custom difficulty panel and slider registry.</summary>
        public IEnumerable<SettingDefinition> ExportAll()
        {
            return _definitions.Values.OrderBy(d => d.Id, StringComparer.Ordinal);
        }
    }
}
