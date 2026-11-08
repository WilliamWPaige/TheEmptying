using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Memory;

namespace TheEmptying.Core.Saves
{
    /// <summary>Saves every flag and counter, in stable (owner index, then name ordinal) order.</summary>
    public static class MemorySection
    {
        public static byte[] Write(MemoryStore store)
        {
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);

            List<(EntityId Owner, string Name, bool Value)> flags = store.AllFlags().ToList();
            writer.WriteInt(flags.Count);
            foreach ((EntityId owner, string name, bool value) in flags)
            {
                writer.WriteEntityId(owner);
                writer.WriteString(name);
                writer.WriteBool(value);
            }

            List<(EntityId Owner, string Name, double Value)> counters = store.AllCounters().ToList();
            writer.WriteInt(counters.Count);
            foreach ((EntityId owner, string name, double value) in counters)
            {
                writer.WriteEntityId(owner);
                writer.WriteString(name);
                writer.WriteDouble(value);
            }

            return stream.ToArray();
        }

        public static void Read(byte[] data, MemoryStore store)
        {
            using var stream = new MemoryStream(data);
            var reader = new SaveReader(stream);

            int flagCount = reader.ReadInt();
            for (int i = 0; i < flagCount; i++)
            {
                EntityId owner = reader.ReadEntityId();
                string name = reader.ReadString();
                bool value = reader.ReadBool();
                store.SetFlag(owner, name, value);
            }

            int counterCount = reader.ReadInt();
            for (int i = 0; i < counterCount; i++)
            {
                EntityId owner = reader.ReadEntityId();
                string name = reader.ReadString();
                double value = reader.ReadDouble();
                store.SetCounter(owner, name, value);
            }
        }
    }
}
