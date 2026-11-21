namespace TheEmptying.Modules.Gathering
{
    /// <summary>What a jobless person's hand-gathering task is doing right now.</summary>
    public enum GatherTaskKind
    {
        /// <summary>No active task (nothing waiting to be built, nothing short, or no passable spot found).</summary>
        None,

        /// <summary>A founder-only task: unpaid labor on a crown construction site waiting for builders.</summary>
        Build,

        Farm,
        Woodcut,
        Quarry
    }
}
