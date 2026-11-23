namespace TheEmptying.Modules.People
{
    /// <summary>R10: one finished action, kept in a person's history for the info window - most recent first.</summary>
    public readonly struct PersonActionHistoryEntry
    {
        public ActionKind Kind { get; }
        public string Thought { get; }
        public long StartTick { get; }
        public long DurationTicks { get; }

        public PersonActionHistoryEntry(ActionKind kind, string thought, long startTick, long durationTicks)
        {
            Kind = kind;
            Thought = thought;
            StartTick = startTick;
            DurationTicks = durationTicks;
        }
    }
}
