namespace TheEmptying.Modules.People
{
    /// <summary>
    /// A person (R7): just a content type and a true world position.
    /// Nothing else yet - no name, needs, job or movement.
    /// </summary>
    public readonly struct PersonState
    {
        public string UnitContentId { get; }
        public double WorldX { get; }
        public double WorldZ { get; }

        public PersonState(string unitContentId, double worldX, double worldZ)
        {
            UnitContentId = unitContentId;
            WorldX = worldX;
            WorldZ = worldZ;
        }
    }
}
