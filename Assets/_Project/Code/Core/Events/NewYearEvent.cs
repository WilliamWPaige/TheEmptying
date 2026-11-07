namespace TheEmptying.Core.Events
{
    public readonly struct NewYearEvent : ISimEvent
    {
        public const string DefinitionIdValue = "event.new_year";

        public long Tick { get; }

        public NewYearEvent(long tick)
        {
            Tick = tick;
        }

        public string DefinitionId => DefinitionIdValue;
    }
}
