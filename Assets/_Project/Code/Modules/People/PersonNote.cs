namespace TheEmptying.Modules.People
{
    /// <summary>R13: one short note in a person's history that isn't tied to a finished action - for now, only "a skill rose to N" ("Foraging rose to 17").</summary>
    public readonly struct PersonNote
    {
        public string Text { get; }
        public long Tick { get; }

        public PersonNote(string text, long tick)
        {
            Text = text;
            Tick = tick;
        }
    }
}
