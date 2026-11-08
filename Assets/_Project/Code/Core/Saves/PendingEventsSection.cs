using System;
using System.Collections.Generic;
using System.IO;
using TheEmptying.Core.Events;

namespace TheEmptying.Core.Saves
{
    /// <summary>
    /// Events posted but not yet delivered. Data events serialize their
    /// parameter bag; typed events need a registered ITypedEventSerializer or
    /// saving fails with a clear error naming the definition Id.
    /// </summary>
    public static class PendingEventsSection
    {
        public static byte[] Write(IReadOnlyList<PostedEvent> events, TypedEventSerializerRegistry serializers)
        {
            using var stream = new MemoryStream();
            var writer = new SaveWriter(stream);

            writer.WriteInt(events.Count);
            foreach (PostedEvent posted in events)
            {
                writer.WriteString(posted.DefinitionId);
                writer.WriteLong(posted.Tick);
                writer.WriteInt(posted.ChainDepth);
                writer.WriteBool(posted.IsTyped);

                if (posted.IsTyped)
                {
                    if (!serializers.TryGet(posted.DefinitionId, out ITypedEventSerializer serializer))
                    {
                        throw new InvalidOperationException($"Cannot save a pending typed event with definition Id '{posted.DefinitionId}': no serializer is registered for it.");
                    }

                    serializer.Write(writer, posted.TypedEvent);
                }
                else
                {
                    EventDataSerializer.Write(writer, posted.Data ?? new EventData());
                }
            }

            return stream.ToArray();
        }

        public static List<PostedEvent> Read(byte[] data, TypedEventSerializerRegistry serializers)
        {
            using var stream = new MemoryStream(data);
            var reader = new SaveReader(stream);

            int count = reader.ReadInt();
            var result = new List<PostedEvent>(count);

            for (int i = 0; i < count; i++)
            {
                string definitionId = reader.ReadString();
                long tick = reader.ReadLong();
                int chainDepth = reader.ReadInt();
                bool isTyped = reader.ReadBool();

                if (isTyped)
                {
                    if (!serializers.TryGet(definitionId, out ITypedEventSerializer serializer))
                    {
                        throw new InvalidOperationException($"Cannot load a pending typed event with definition Id '{definitionId}': no serializer is registered for it.");
                    }

                    ISimEvent typedEvent = serializer.Read(reader);
                    result.Add(new PostedEvent(definitionId, tick, typedEvent, null, chainDepth));
                }
                else
                {
                    EventData eventData = EventDataSerializer.Read(reader);
                    result.Add(new PostedEvent(definitionId, tick, null, eventData, chainDepth));
                }
            }

            return result;
        }
    }
}
