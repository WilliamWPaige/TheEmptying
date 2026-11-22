namespace TheEmptying.Modules.Gathering
{
    /// <summary>Which leg of a haul trip a Haul-tasked person is currently on.</summary>
    public enum HaulPhase
    {
        /// <summary>Walking to the crown stockpile to pick up a load (nothing carried yet).</summary>
        ToStockpile,

        /// <summary>Walking to the construction site to drop off the load already picked up.</summary>
        ToSite
    }
}
