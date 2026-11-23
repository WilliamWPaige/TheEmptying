namespace TheEmptying.Modules.People
{
    /// <summary>
    /// R10: every kind of action a person can be doing. Idle and Walk are
    /// the only two for now; Chop, Build, Haul, Sleep and others are meant
    /// to be added here later without the action system itself (picking
    /// the next action, tracking progress, saving) needing to change.
    /// </summary>
    public enum ActionKind
    {
        Idle,
        Walk
    }
}
