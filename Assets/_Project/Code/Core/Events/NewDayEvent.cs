namespace TheEmptying.Core.Events
{
    public readonly struct NewDayEvent : ISimEvent
    {
        public const string DefinitionIdValue = "event.new_day";

        public long Tick { get; }

        public NewDayEvent(long tick)
        {
            Tick = tick;
        }

        public string DefinitionId => DefinitionIdValue;
    }
}
