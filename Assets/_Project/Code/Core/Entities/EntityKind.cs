namespace TheEmptying.Core.Entities
{
    public enum EntityKind
    {
        Person,
        Building,
        Item,
        Group,
        Site,
        Zone,

        /// <summary>R17: a pond - the only resource with mutable per-instance state (current drinks), so it's a real entity rather than a stateless map placement.</summary>
        Pond
    }
}
