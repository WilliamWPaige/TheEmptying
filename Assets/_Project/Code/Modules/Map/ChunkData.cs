namespace TheEmptying.Modules.Map
{
    /// <summary>
    /// One chunk's data. Bare by design (R6) - the ground is flat and plain
    /// for now, so there is nothing to store yet beyond the chunk existing.
    /// Later systems (terrain shaping, what's at a cell) add their own
    /// fields here.
    /// </summary>
    public sealed class ChunkData
    {
        public ChunkCoord Coord { get; }

        public ChunkData(ChunkCoord coord)
        {
            Coord = coord;
        }
    }
}
