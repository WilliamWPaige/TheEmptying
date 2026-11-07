using TheEmptying.Core.Events;

namespace TheEmptying.Core.Saves
{
    /// <summary>Serializers for the four calendar events, which the engine depends on.</summary>
    public static class CalendarEventSerializers
    {
        public static void RegisterAll(TypedEventSerializerRegistry registry)
        {
            registry.Register(NewHourEvent.DefinitionIdValue, new NewHourEventSerializer());
            registry.Register(NewDayEvent.DefinitionIdValue, new NewDayEventSerializer());
            registry.Register(NewSeasonEvent.DefinitionIdValue, new NewSeasonEventSerializer());
            registry.Register(NewYearEvent.DefinitionIdValue, new NewYearEventSerializer());
        }

        private sealed class NewHourEventSerializer : ITypedEventSerializer
        {
            public void Write(SaveWriter writer, ISimEvent typedEvent) => writer.WriteLong(((NewHourEvent)typedEvent).Tick);
            public ISimEvent Read(SaveReader reader) => new NewHourEvent(reader.ReadLong());
        }

        private sealed class NewDayEventSerializer : ITypedEventSerializer
        {
            public void Write(SaveWriter writer, ISimEvent typedEvent) => writer.WriteLong(((NewDayEvent)typedEvent).Tick);
            public ISimEvent Read(SaveReader reader) => new NewDayEvent(reader.ReadLong());
        }

        private sealed class NewSeasonEventSerializer : ITypedEventSerializer
        {
            public void Write(SaveWriter writer, ISimEvent typedEvent) => writer.WriteLong(((NewSeasonEvent)typedEvent).Tick);
            public ISimEvent Read(SaveReader reader) => new NewSeasonEvent(reader.ReadLong());
        }

        private sealed class NewYearEventSerializer : ITypedEventSerializer
        {
            public void Write(SaveWriter writer, ISimEvent typedEvent) => writer.WriteLong(((NewYearEvent)typedEvent).Tick);
            public ISimEvent Read(SaveReader reader) => new NewYearEvent(reader.ReadLong());
        }
    }
}
