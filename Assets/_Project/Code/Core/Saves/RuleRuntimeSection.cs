using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Core.Events;
using TheEmptying.Core.Rules;

namespace TheEmptying.Core.Saves
{
    /// <summary>
    /// Saves the rule runtime: the instance Id counter and every paused
    /// instance (rule Id, resume tick, position, locals, triggering event).
    /// Typed triggering events use the existing typed event serializers; data
    /// events use the existing data event serializer.
    /// </summary>
    public static class RuleRuntimeSection
    {
        public static byte[] Write(long nextInstanceId, IEnumerable<(string RuleId, long ResumeTick, RuleInstance Instance)> paused, TypedEventSerializerRegistry serializers)
        {
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);

            writer.WriteLong(nextInstanceId);

            List<(string RuleId, long ResumeTick, RuleInstance Instance)> list = paused.ToList();
            writer.WriteInt(list.Count);

            foreach ((string ruleId, long resumeTick, RuleInstance instance) in list)
            {
                writer.WriteString(ruleId);
                writer.WriteLong(resumeTick);
                writer.WriteLong(instance.InstanceId);
                writer.WriteInt(instance.PostedEventChainDepth);

                WritePosition(writer, instance.Position);
                WriteLocals(writer, instance.Locals);
                WriteTriggeringEvent(writer, ruleId, instance.TriggeringEvent, serializers);
            }

            return stream.ToArray();
        }

        public static (long NextInstanceId, List<(string RuleId, long ResumeTick, RuleInstance Instance)> Paused) Read(byte[] data, TypedEventSerializerRegistry serializers)
        {
            using var stream = new MemoryStream(data);
            var reader = new SaveReader(stream);

            long nextInstanceId = reader.ReadLong();
            int count = reader.ReadInt();
            var result = new List<(string, long, RuleInstance)>(count);

            for (int i = 0; i < count; i++)
            {
                string ruleId = reader.ReadString();
                long resumeTick = reader.ReadLong();
                long instanceId = reader.ReadLong();
                int postedEventChainDepth = reader.ReadInt();

                RuleExecutionPosition position = ReadPosition(reader);
                Dictionary<string, RuleValue> locals = ReadLocals(reader);
                PostedEvent triggeringEvent = ReadTriggeringEvent(reader, serializers);

                var instance = new RuleInstance(instanceId, ruleId, triggeringEvent, postedEventChainDepth);
                instance.Position.CurrentNodeId = position.CurrentNodeId;
                instance.Position.ContinuationStack.AddRange(position.ContinuationStack);
                foreach (KeyValuePair<string, RuleValue> local in locals)
                {
                    instance.Locals[local.Key] = local.Value;
                }

                result.Add((ruleId, resumeTick, instance));
            }

            return (nextInstanceId, result);
        }

        private static void WritePosition(SaveWriter writer, RuleExecutionPosition position)
        {
            writer.WriteString(position.CurrentNodeId);
            writer.WriteInt(position.ContinuationStack.Count);

            foreach (RuleContinuationFrame frame in position.ContinuationStack)
            {
                writer.WriteString(frame.NodeId);
                writer.WriteStringList(frame.RemainingPorts);
            }
        }

        private static RuleExecutionPosition ReadPosition(SaveReader reader)
        {
            var position = new RuleExecutionPosition { CurrentNodeId = reader.ReadString() };
            int frameCount = reader.ReadInt();

            for (int i = 0; i < frameCount; i++)
            {
                string nodeId = reader.ReadString();
                List<string> ports = reader.ReadStringList();
                position.ContinuationStack.Add(new RuleContinuationFrame(nodeId, ports));
            }

            return position;
        }

        private static void WriteLocals(SaveWriter writer, IReadOnlyDictionary<string, RuleValue> locals)
        {
            List<string> names = locals.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
            writer.WriteInt(names.Count);

            foreach (string name in names)
            {
                writer.WriteString(name);
                RuleValueSerializer.Write(writer, locals[name]);
            }
        }

        private static Dictionary<string, RuleValue> ReadLocals(SaveReader reader)
        {
            int count = reader.ReadInt();
            var locals = new Dictionary<string, RuleValue>(count);

            for (int i = 0; i < count; i++)
            {
                string name = reader.ReadString();
                locals[name] = RuleValueSerializer.Read(reader);
            }

            return locals;
        }

        private static void WriteTriggeringEvent(SaveWriter writer, string ruleId, PostedEvent triggeringEvent, TypedEventSerializerRegistry serializers)
        {
            bool hasTriggeringEvent = triggeringEvent != null;
            writer.WriteBool(hasTriggeringEvent);

            if (!hasTriggeringEvent)
            {
                return;
            }

            writer.WriteString(triggeringEvent.DefinitionId);
            writer.WriteLong(triggeringEvent.Tick);
            writer.WriteInt(triggeringEvent.ChainDepth);
            writer.WriteBool(triggeringEvent.IsTyped);

            if (triggeringEvent.IsTyped)
            {
                if (!serializers.TryGet(triggeringEvent.DefinitionId, out ITypedEventSerializer serializer))
                {
                    throw new InvalidOperationException($"Cannot save paused rule instance for rule '{ruleId}': no serializer is registered for typed event '{triggeringEvent.DefinitionId}'.");
                }

                serializer.Write(writer, triggeringEvent.TypedEvent);
            }
            else
            {
                EventDataSerializer.Write(writer, triggeringEvent.Data ?? new EventData());
            }
        }

        private static PostedEvent ReadTriggeringEvent(SaveReader reader, TypedEventSerializerRegistry serializers)
        {
            bool hasTriggeringEvent = reader.ReadBool();
            if (!hasTriggeringEvent)
            {
                return null;
            }

            string definitionId = reader.ReadString();
            long tick = reader.ReadLong();
            int chainDepth = reader.ReadInt();
            bool isTyped = reader.ReadBool();

            if (isTyped)
            {
                if (!serializers.TryGet(definitionId, out ITypedEventSerializer serializer))
                {
                    throw new InvalidOperationException($"Cannot load a paused rule instance: no serializer is registered for typed event '{definitionId}'.");
                }

                ISimEvent typedEvent = serializer.Read(reader);
                return new PostedEvent(definitionId, tick, typedEvent, null, chainDepth);
            }

            EventData data = EventDataSerializer.Read(reader);
            return new PostedEvent(definitionId, tick, null, data, chainDepth);
        }
    }
}
