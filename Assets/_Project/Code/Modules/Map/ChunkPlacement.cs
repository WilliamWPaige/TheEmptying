namespace TheEmptying.Modules.Map
{
    /// <summary>TEMPORARY (R6b): one random tree or stone placed in a chunk when it was first created.</summary>
    public readonly struct ChunkPlacement
    {
        public PlacementKind Kind { get; }
        public CellCoord Cell { get; }

        public ChunkPlacement(PlacementKind kind, CellCoord cell)
        {
            Kind = kind;
            Cell = cell;
        }
    }
}
