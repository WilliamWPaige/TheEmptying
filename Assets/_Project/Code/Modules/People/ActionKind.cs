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
        Walk,

        /// <summary>R11: waiting inside a building (the keep, at founding) for their turn to head out - not drawn, and never ends on its own; only the keep-exit timer ends it.</summary>
        InsideBuilding
    }
}
