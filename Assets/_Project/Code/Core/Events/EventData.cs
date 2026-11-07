using System.Collections.Generic;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Events
{
    /// <summary>A small named parameter bag for data events, which have no C# type of their own.</summary>
    public sealed class EventData
    {
        private Dictionary<string, EntityId> _entities;
        private Dictionary<string, long> _longs;
        private Dictionary<string, double> _doubles;
        private Dictionary<string, bool> _bools;
        private Dictionary<string, string> _strings;

        public EventData SetEntity(string name, EntityId value)
        {
            (_entities ??= new Dictionary<string, EntityId>())[name] = value;
            return this;
        }

        public EventData SetLong(string name, long value)
        {
            (_longs ??= new Dictionary<string, long>())[name] = value;
            return this;
        }

        public EventData SetDouble(string name, double value)
        {
            (_doubles ??= new Dictionary<string, double>())[name] = value;
            return this;
        }

        public EventData SetBool(string name, bool value)
        {
            (_bools ??= new Dictionary<string, bool>())[name] = value;
            return this;
        }

        public EventData SetString(string name, string value)
        {
            (_strings ??= new Dictionary<string, string>())[name] = value;
            return this;
        }

        public bool TryGetEntity(string name, out EntityId value)
        {
            if (_entities != null && _entities.TryGetValue(name, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetLong(string name, out long value)
        {
            if (_longs != null && _longs.TryGetValue(name, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetDouble(string name, out double value)
        {
            if (_doubles != null && _doubles.TryGetValue(name, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetBool(string name, out bool value)
        {
            if (_bools != null && _bools.TryGetValue(name, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetString(string name, out string value)
        {
            if (_strings != null && _strings.TryGetValue(name, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        public EntityId GetEntity(string name) => TryGetEntity(name, out EntityId value) ? value : throw Missing(name);
        public long GetLong(string name) => TryGetLong(name, out long value) ? value : throw Missing(name);
        public double GetDouble(string name) => TryGetDouble(name, out double value) ? value : throw Missing(name);
        public bool GetBool(string name) => TryGetBool(name, out bool value) ? value : throw Missing(name);
        public string GetString(string name) => TryGetString(name, out string value) ? value : throw Missing(name);

        /// <summary>Read-only views for serialization. Never null, even when nothing of that type was set.</summary>
        public IReadOnlyDictionary<string, EntityId> EntityValues => _entities ?? (IReadOnlyDictionary<string, EntityId>)EmptyEntities;
        public IReadOnlyDictionary<string, long> LongValues => _longs ?? (IReadOnlyDictionary<string, long>)EmptyLongs;
        public IReadOnlyDictionary<string, double> DoubleValues => _doubles ?? (IReadOnlyDictionary<string, double>)EmptyDoubles;
        public IReadOnlyDictionary<string, bool> BoolValues => _bools ?? (IReadOnlyDictionary<string, bool>)EmptyBools;
        public IReadOnlyDictionary<string, string> StringValues => _strings ?? (IReadOnlyDictionary<string, string>)EmptyStrings;

        private static readonly Dictionary<string, EntityId> EmptyEntities = new Dictionary<string, EntityId>();
        private static readonly Dictionary<string, long> EmptyLongs = new Dictionary<string, long>();
        private static readonly Dictionary<string, double> EmptyDoubles = new Dictionary<string, double>();
        private static readonly Dictionary<string, bool> EmptyBools = new Dictionary<string, bool>();
        private static readonly Dictionary<string, string> EmptyStrings = new Dictionary<string, string>();

        private static KeyNotFoundException Missing(string name)
        {
            return new KeyNotFoundException($"EventData has no value named '{name}'.");
        }
    }
}
