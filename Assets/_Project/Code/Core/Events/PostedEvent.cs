namespace TheEmptying.Core.Events
{
    /// <summary>
    /// What definition-ID subscribers receive, and what the bus queues internally.
    /// Exactly one of TypedEvent or Data is set.
    /// </summary>
    public sealed class PostedEvent
    {
        public string DefinitionId { get; }
        public long Tick { get; }
        public ISimEvent TypedEvent { get; }
        public EventData Data { get; }

        public PostedEvent(string definitionId, long tick, ISimEvent typedEvent, EventData data)
        {
            DefinitionId = definitionId;
            Tick = tick;
            TypedEvent = typedEvent;
            Data = data;
        }

        public bool IsTyped => TypedEvent != null;
    }
}
