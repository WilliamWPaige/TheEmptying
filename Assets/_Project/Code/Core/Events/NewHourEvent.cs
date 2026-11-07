namespace TheEmptying.Core.Events
{
    public readonly struct NewHourEvent : ISimEvent
    {
        public const string DefinitionIdValue = "event.new_hour";

        public long Tick { get; }

        public NewHourEvent(long tick)
        {
            Tick = tick;
        }

        public string DefinitionId => DefinitionIdValue;
    }
}
