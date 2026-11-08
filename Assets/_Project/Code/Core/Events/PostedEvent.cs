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

        /// <summary>0 for events posted by code; an event posted by a rule gets the triggering event's depth plus 1.</summary>
        public int ChainDepth { get; }

        public PostedEvent(string definitionId, long tick, ISimEvent typedEvent, EventData data, int chainDepth = 0)
        {
            DefinitionId = definitionId;
            Tick = tick;
            TypedEvent = typedEvent;
            Data = data;
            ChainDepth = chainDepth;
        }

        public bool IsTyped => TypedEvent != null;
    }
}
