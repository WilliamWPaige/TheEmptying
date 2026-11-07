namespace TheEmptying.Core.Events
{
    public readonly struct NewSeasonEvent : ISimEvent
    {
        public const string DefinitionIdValue = "event.new_season";

        public long Tick { get; }

        public NewSeasonEvent(long tick)
        {
            Tick = tick;
        }

        public string DefinitionId => DefinitionIdValue;
    }
}
