using System.Collections.Generic;

namespace TheEmptying.Modules.Map
{
    /// <summary>
    /// One chunk's data. Mostly bare by design (R6) - the ground is flat
    /// and plain for now. Placements (R6b) are a TEMPORARY stand-in for
    /// real map content until the real map design exists; later systems
    /// add their own fields here.
    /// </summary>
    public sealed class ChunkData
    {
        public ChunkCoord Coord { get; }
        public IReadOnlyList<ChunkPlacement> Placements { get; }

        public ChunkData(ChunkCoord coord, IReadOnlyList<ChunkPlacement> placements)
        {
            Coord = coord;
            Placements = placements;
        }
    }
}
