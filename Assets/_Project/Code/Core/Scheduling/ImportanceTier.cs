namespace TheEmptying.Core.Scheduling
{
    /// <summary>
    /// Normal is the default (enum value 0): if a system's tier callback doesn't
    /// single out an entity, it is treated as Normal.
    /// </summary>
    public enum ImportanceTier
    {
        Normal = 0,
        High = 1,
        Low = 2
    }
}
