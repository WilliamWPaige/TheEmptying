using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;

namespace TheEmptying.Core.Saves
{
    /// <summary>Serializes an EventData parameter bag. Keys are written in ordinal order for a stable byte stream.</summary>
    public static class EventDataSerializer
    {
        public static void Write(SaveWriter writer, EventData data)
        {
            WriteMap(writer, data.EntityValues, (w, v) => w.WriteEntityId(v));
            WriteMap(writer, data.LongValues, (w, v) => w.WriteLong(v));
            WriteMap(writer, data.DoubleValues, (w, v) => w.WriteDouble(v));
            WriteMap(writer, data.BoolValues, (w, v) => w.WriteBool(v));
            WriteMap(writer, data.StringValues, (w, v) => w.WriteString(v));
        }

        public static EventData Read(SaveReader reader)
        {
            var data = new EventData();

            foreach ((string name, EntityId value) in ReadMap(reader, r => r.ReadEntityId()))
            {
                data.SetEntity(name, value);
            }

            foreach ((string name, long value) in ReadMap(reader, r => r.ReadLong()))
            {
                data.SetLong(name, value);
            }

            foreach ((string name, double value) in ReadMap(reader, r => r.ReadDouble()))
            {
                data.SetDouble(name, value);
            }

            foreach ((string name, bool value) in ReadMap(reader, r => r.ReadBool()))
            {
                data.SetBool(name, value);
            }

            foreach ((string name, string value) in ReadMap(reader, r => r.ReadString()))
            {
                data.SetString(name, value);
            }

            return data;
        }

        private static void WriteMap<T>(SaveWriter writer, IReadOnlyDictionary<string, T> map, Action<SaveWriter, T> writeValue)
        {
            List<string> keys = map.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            writer.WriteInt(keys.Count);
            foreach (string key in keys)
            {
                writer.WriteString(key);
                writeValue(writer, map[key]);
            }
        }

        private static IEnumerable<(string, T)> ReadMap<T>(SaveReader reader, Func<SaveReader, T> readValue)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                string name = reader.ReadString();
                T value = readValue(reader);
                yield return (name, value);
            }
        }
    }
}
