namespace TheEmptying.Core.Events
{
    public readonly struct NewMonthEvent : ISimEvent
    {
        public const string DefinitionIdValue = "event.new_month";

        public long Tick { get; }

        public NewMonthEvent(long tick)
        {
            Tick = tick;
        }

        public string DefinitionId => DefinitionIdValue;
    }
}
