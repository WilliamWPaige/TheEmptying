using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Randomness;

namespace TheEmptying.Core.Saves
{
    /// <summary>
    /// Writes and reads the core sections: clock (current tick), random service
    /// (every stream's state), entity registry state, settings values, and
    /// pending events. Scheduler registrations and event subscriptions are not
    /// saved; modules recreate them when they initialize. Content is not saved;
    /// it reloads from the sources.
    /// </summary>
    public static class CoreSections
    {
        public const string ClockId = "core.clock";
        public const string RandomId = "core.random";
        public const string EntitiesId = "core.entities";
        public const string SettingsId = "core.settings";
        public const string PendingEventsId = "core.pending_events";
        public const string RuleRuntimeId = "core.rule_runtime";
        public const string MemoryId = "core.memory";

        public static readonly string[] AllIds = { ClockId, RandomId, EntitiesId, SettingsId, PendingEventsId, RuleRuntimeId, MemoryId };

        public static List<SaveSectionData> WriteAll(Simulation simulation, SettingsRegistry settings, TypedEventSerializerRegistry eventSerializers)
        {
            return new List<SaveSectionData>
            {
                new SaveSectionData(ClockId, 1, WriteClock(simulation)),
                new SaveSectionData(RandomId, 1, WriteRandom(simulation)),
                new SaveSectionData(EntitiesId, 1, WriteEntities(simulation)),
                new SaveSectionData(SettingsId, 1, WriteSettings(settings)),
                new SaveSectionData(PendingEventsId, 1, PendingEventsSection.Write(simulation.Events.ExportPending(), eventSerializers)),
                new SaveSectionData(RuleRuntimeId, 1, WriteRuleRuntime(simulation, eventSerializers)),
                new SaveSectionData(MemoryId, 1, MemorySection.Write(simulation.Memory))
            };
        }

        public static void ReadAll(IReadOnlyDictionary<string, SaveSectionData> sections, Simulation simulation, SettingsRegistry settings, TypedEventSerializerRegistry eventSerializers)
        {
            if (sections.TryGetValue(ClockId, out SaveSectionData clock))
            {
                ReadClock(clock.Data, simulation);
            }

            if (sections.TryGetValue(RandomId, out SaveSectionData random))
            {
                ReadRandom(random.Data, simulation);
            }

            if (sections.TryGetValue(EntitiesId, out SaveSectionData entities))
            {
                ReadEntities(entities.Data, simulation);
            }

            if (sections.TryGetValue(SettingsId, out SaveSectionData settingsSection))
            {
                ReadSettings(settingsSection.Data, settings);
            }

            if (sections.TryGetValue(PendingEventsId, out SaveSectionData pending))
            {
                List<PostedEvent> events = PendingEventsSection.Read(pending.Data, eventSerializers);
                simulation.Events.RestorePending(events);
            }

            if (sections.TryGetValue(RuleRuntimeId, out SaveSectionData ruleRuntime) && simulation.RuleRunner != null)
            {
                (long nextInstanceId, var paused) = RuleRuntimeSection.Read(ruleRuntime.Data, eventSerializers);
                simulation.RuleRunner.RestorePausedState(nextInstanceId, paused);
            }

            if (sections.TryGetValue(MemoryId, out SaveSectionData memory))
            {
                MemorySection.Read(memory.Data, simulation.Memory);
            }
        }

        private static byte[] WriteRuleRuntime(Simulation simulation, TypedEventSerializerRegistry eventSerializers)
        {
            if (simulation.RuleRunner == null)
            {
                using var stream = new MemoryStream();
                var writer = new SaveWriter(stream);
                writer.WriteLong(1);
                writer.WriteInt(0);
                return stream.ToArray();
            }

            return RuleRuntimeSection.Write(simulation.RuleRunner.NextInstanceId, simulation.RuleRunner.PausedInstances, eventSerializers);
        }

        private static byte[] WriteClock(Simulation sim)
        {
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            writer.WriteLong(sim.Clock.CurrentTick);
            return stream.ToArray();
        }

        private static void ReadClock(byte[] data, Simulation sim)
        {
            using var stream = new MemoryStream(data);
            var reader = new SaveReader(stream);
            sim.Clock.RestoreTick(reader.ReadLong());
        }

        private static byte[] WriteRandom(Simulation sim)
        {
            RandomServiceState state = sim.Random.ExportState();
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            writer.WriteInt(state.Streams.Count);
            foreach (RandomStreamEntry entry in state.Streams)
            {
                writer.WriteString(entry.Name);
                writer.WriteULong(entry.State);
                writer.WriteULong(entry.Inc);
            }

            return stream.ToArray();
        }

        private static void ReadRandom(byte[] data, Simulation sim)
        {
            using var stream = new MemoryStream(data);
            var reader = new SaveReader(stream);
            int count = reader.ReadInt();
            var state = new RandomServiceState();
            for (int i = 0; i < count; i++)
            {
                string name = reader.ReadString();
                ulong streamState = reader.ReadULong();
                ulong inc = reader.ReadULong();
                state.Streams.Add(new RandomStreamEntry { Name = name, State = streamState, Inc = inc });
            }

            sim.Random.RestoreState(state);
        }

        private static byte[] WriteEntities(Simulation sim)
        {
            EntityRegistryState state = sim.Entities.ExportState();
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            writer.WriteIntList(state.Generations);
            writer.WriteBoolList(state.Alive);
            writer.WriteIntList(state.Kinds.Select(k => (int)k).ToList());
            writer.WriteIntList(state.FreeIndices);
            return stream.ToArray();
        }

        private static void ReadEntities(byte[] data, Simulation sim)
        {
            using var stream = new MemoryStream(data);
            var reader = new SaveReader(stream);
            var state = new EntityRegistryState
            {
                Generations = reader.ReadIntList(),
                Alive = reader.ReadBoolList(),
                Kinds = reader.ReadIntList().Select(i => (EntityKind)i).ToList(),
                FreeIndices = reader.ReadIntList()
            };
            sim.Entities.RestoreState(state);
        }

        private static byte[] WriteSettings(SettingsRegistry settings)
        {
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);
            List<SettingDefinition> definitions = settings.ExportAll().ToList();
            writer.WriteInt(definitions.Count);
            foreach (SettingDefinition definition in definitions)
            {
                writer.WriteString(definition.Id);
                writer.WriteDouble(settings.Get(definition.Id));
            }

            return stream.ToArray();
        }

        private static void ReadSettings(byte[] data, SettingsRegistry settings)
        {
            using var stream = new MemoryStream(data);
            var reader = new SaveReader(stream);
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                double value = reader.ReadDouble();
                if (settings.IsDeclared(id))
                {
                    settings.Set(id, value);
                }
            }
        }
    }
}
