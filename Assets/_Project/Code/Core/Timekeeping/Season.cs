namespace TheEmptying.Core.Timekeeping
{
    /// <summary>
    /// Waking, Bloom, Reaping and Frost each cover 3 months; the Hollow is the 13th
    /// month alone, counting as its own season for anything reacting to season changes.
    /// </summary>
    public enum Season
    {
        Waking = 0,
        Bloom = 1,
        Reaping = 2,
        Frost = 3,
        Hollow = 4
    }
}
