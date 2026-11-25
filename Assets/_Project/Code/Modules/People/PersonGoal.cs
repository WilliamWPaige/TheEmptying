namespace TheEmptying.Modules.People
{
    /// <summary>
    /// R13: what a person is currently trying to accomplish, driving which
    /// action comes next once the current one ends (or, at zero hunger,
    /// even before it ends). Re-evaluated from scratch - position, hunger,
    /// carried goods - every time an action ends, rather than tracking a
    /// detailed sub-step, so an interrupted trip resumes cleanly.
    /// </summary>
    public enum PersonGoal
    {
        /// <summary>Nothing urgent - idle/wander, or (if eligible) forage for the stockpile.</summary>
        None,

        /// <summary>Hungry (below the eat threshold, or at zero): walking to and eating at the nearest reachable food source, repeating until full.</summary>
        SeekingFood,

        /// <summary>Just became full at a mound, and home stash is low: dig extra up to carry amount, then walk home and drop it off.</summary>
        ToppingUpHomeStash,

        /// <summary>Work, chosen while not hungry: dig at a mound up to carry amount or the target stockpile's room, walk there, drop off.</summary>
        ForagingForStockpile,

        /// <summary>R17: thirsty (below the drink threshold, or at zero): walking to and drinking at the nearest reachable non-dry pond, until full. Whichever of hunger and thirst is lower is served first.</summary>
        SeekingWater
    }
}
