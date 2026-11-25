using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;

namespace TheEmptying.Modules.Map
{
    /// <summary>
    /// The world grid (R6): world size, cell size and chunk size (all
    /// settings, read live - never cached - since they can change between
    /// tests or a settings edit), and the per-chunk data store other
    /// systems ask "what is at this cell" through later. Chunks are
    /// created lazily, on first use, rather than filling a fixed array for
    /// the whole 15 km world - at 1 meter cells that would be 225 million
    /// entries for a world that is still just flat, empty ground. R6b adds
    /// a TEMPORARY random tree/stone placement, generated once per chunk
    /// from the world seed and the chunk's own coordinates alone, so it
    /// never depends on visit order - see GeneratePlacements. R10: every
    /// created chunk's placements are saved (ISaveableModule) - a chunk
    /// already generated before a save must never be regenerated after a
    /// load, since its named random stream has already moved past the
    /// point that produced the saved placements; regenerating it would
    /// both give different (wrong) placements and double-consume that
    /// stream.
    /// </summary>
    public sealed class MapModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.map";

        public const string WorldWidthSettingId = "setting.map.world_width_meters";
        public const string WorldDepthSettingId = "setting.map.world_depth_meters";
        public const string CellSizeSettingId = "setting.map.cell_size_meters";
        public const string ChunkSizeSettingId = "setting.map.chunk_size_meters";
        public const string TreesPerChunkSettingId = "setting.map.trees_per_chunk";
        public const string StonesPerChunkSettingId = "setting.map.stones_per_chunk";
        public const string GrubMoundsPerChunkSettingId = "setting.map.grub_mounds_per_chunk";

        private const int MaxPlacementAttempts = 50;

        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new Dictionary<ChunkCoord, ChunkData>();

        private SettingsRegistry _settings;
        private RandomService _randomService;

        public string Id => ModuleId;
        public int Version => 1;
        public int SectionVersion => 1;
        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public double WorldWidthMeters => _settings.Get(WorldWidthSettingId);
        public double WorldDepthMeters => _settings.Get(WorldDepthSettingId);
        public double CellSizeMeters => _settings.Get(CellSizeSettingId);
        public double ChunkSizeMeters => _settings.Get(ChunkSizeSettingId);

        public int ChunkCountX => (int)Math.Ceiling(WorldWidthMeters / ChunkSizeMeters);
        public int ChunkCountZ => (int)Math.Ceiling(WorldDepthMeters / ChunkSizeMeters);

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _randomService = context.RandomService;
        }

        public void Shutdown()
        {
            _chunks.Clear();
        }

        public CellCoord WorldToCell(double worldX, double worldZ)
        {
            double cellSize = CellSizeMeters;
            return new CellCoord((int)Math.Floor(worldX / cellSize), (int)Math.Floor(worldZ / cellSize));
        }

        public ChunkCoord CellToChunk(CellCoord cell)
        {
            double cellsPerChunk = ChunkSizeMeters / CellSizeMeters;
            return new ChunkCoord((int)Math.Floor(cell.X / cellsPerChunk), (int)Math.Floor(cell.Z / cellsPerChunk));
        }

        public ChunkCoord WorldToChunk(double worldX, double worldZ)
        {
            double chunkSize = ChunkSizeMeters;
            return new ChunkCoord((int)Math.Floor(worldX / chunkSize), (int)Math.Floor(worldZ / chunkSize));
        }

        /// <summary>The chunk's own origin corner (lowest X/Z), in true world meters.</summary>
        public (double X, double Z) ChunkOrigin(ChunkCoord chunk) => (chunk.X * ChunkSizeMeters, chunk.Z * ChunkSizeMeters);

        /// <summary>Keeps a world position inside the world's edges - the camera's impassable border.</summary>
        public (double X, double Z) ClampToWorld(double worldX, double worldZ)
        {
            double clampedX = Math.Clamp(worldX, 0, WorldWidthMeters);
            double clampedZ = Math.Clamp(worldZ, 0, WorldDepthMeters);
            return (clampedX, clampedZ);
        }

        public bool TryGetChunk(ChunkCoord coord, out ChunkData chunk) => _chunks.TryGetValue(coord, out chunk);

        /// <summary>Whether a cell is free of any placement (tree, stone or grub mound) in its chunk - creating that chunk if it doesn't exist yet.</summary>
        public bool IsCellFree(CellCoord cell)
        {
            ChunkData chunk = GetOrCreateChunk(CellToChunk(cell));

            foreach (ChunkPlacement placement in chunk.Placements)
            {
                int footprint = FootprintCellsFor(placement.Kind);
                if (cell.X >= placement.Cell.X && cell.X < placement.Cell.X + footprint &&
                    cell.Z >= placement.Cell.Z && cell.Z < placement.Cell.Z + footprint)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// R11: removes any placement (tree, stone or grub mound) whose own
        /// footprint overlaps the given cell rectangle (min inclusive, max
        /// exclusive) - used once, when the keep is founded on top of
        /// whatever was already there. Assumes the rectangle is smaller
        /// than a chunk (true for any footprint used so far), so only the
        /// chunks its four corners fall into can possibly need touching.
        /// </summary>
        public void RemovePlacementsOverlapping(CellCoord min, CellCoord maxExclusive)
        {
            var touchedChunks = new HashSet<ChunkCoord>
            {
                CellToChunk(min),
                CellToChunk(new CellCoord(maxExclusive.X - 1, min.Z)),
                CellToChunk(new CellCoord(min.X, maxExclusive.Z - 1)),
                CellToChunk(new CellCoord(maxExclusive.X - 1, maxExclusive.Z - 1))
            };

            foreach (ChunkCoord coord in touchedChunks)
            {
                ChunkData chunk = GetOrCreateChunk(coord);
                var kept = new List<ChunkPlacement>();
                bool changed = false;

                foreach (ChunkPlacement placement in chunk.Placements)
                {
                    int footprint = FootprintCellsFor(placement.Kind);
                    bool overlaps = placement.Cell.X < maxExclusive.X && placement.Cell.X + footprint > min.X &&
                                     placement.Cell.Z < maxExclusive.Z && placement.Cell.Z + footprint > min.Z;

                    if (overlaps)
                    {
                        changed = true;
                    }
                    else
                    {
                        kept.Add(placement);
                    }
                }

                if (changed)
                {
                    _chunks[coord] = new ChunkData(coord, kept);
                }
            }
        }

        /// <summary>
        /// R17b: removes any placement (tree, stone or grub mound) whose
        /// own footprint comes within radiusMeters of (centerX, centerZ) -
        /// used when a pond is created, so its water and a clear margin
        /// around it never have anything blocking an edge cell. Each
        /// touched chunk is forced to exist first (GetOrCreateChunk), so
        /// the result is the same whether this chunk's placements were
        /// already generated or not - and, like RemovePlacementsOverlapping,
        /// only the chunks the bounding square's four corners fall into are
        /// touched (true for every pond size used so far).
        /// </summary>
        public void RemovePlacementsWithinRadius(double centerX, double centerZ, double radiusMeters)
        {
            double cellSize = CellSizeMeters;

            var touchedChunks = new HashSet<ChunkCoord>
            {
                WorldToChunk(centerX - radiusMeters, centerZ - radiusMeters),
                WorldToChunk(centerX + radiusMeters, centerZ - radiusMeters),
                WorldToChunk(centerX - radiusMeters, centerZ + radiusMeters),
                WorldToChunk(centerX + radiusMeters, centerZ + radiusMeters)
            };

            foreach (ChunkCoord coord in touchedChunks)
            {
                ChunkData chunk = GetOrCreateChunk(coord);
                var kept = new List<ChunkPlacement>();
                bool changed = false;

                foreach (ChunkPlacement placement in chunk.Placements)
                {
                    int footprint = FootprintCellsFor(placement.Kind);
                    double rectMinX = placement.Cell.X * cellSize;
                    double rectMinZ = placement.Cell.Z * cellSize;
                    double rectMaxX = rectMinX + footprint * cellSize;
                    double rectMaxZ = rectMinZ + footprint * cellSize;

                    double closestX = Math.Clamp(centerX, rectMinX, rectMaxX);
                    double closestZ = Math.Clamp(centerZ, rectMinZ, rectMaxZ);
                    double dx = centerX - closestX;
                    double dz = centerZ - closestZ;
                    bool withinRadius = dx * dx + dz * dz < radiusMeters * radiusMeters;

                    if (withinRadius)
                    {
                        changed = true;
                    }
                    else
                    {
                        kept.Add(placement);
                    }
                }

                if (changed)
                {
                    _chunks[coord] = new ChunkData(coord, kept);
                }
            }
        }

        /// <summary>Creates the chunk's data (and its one-time random placements) the first time it's asked for; returns the same instance on every later call.</summary>
        public ChunkData GetOrCreateChunk(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out ChunkData chunk))
            {
                chunk = new ChunkData(coord, GeneratePlacements(coord));
                _chunks[coord] = chunk;
            }

            return chunk;
        }

        /// <summary>The footprint (in cells per side) a placement of this kind occupies - resource.stone is 2x2, everything else here is 1x1.</summary>
        private static int FootprintCellsFor(PlacementKind kind) => kind == PlacementKind.Stone ? 2 : 1;

        /// <summary>
        /// TEMPORARY (R6b/R7): trees, stones and grub mounds scattered at
        /// random cells, a stand-in until the real map design exists. Uses
        /// a stream named for this exact chunk, so the result depends only
        /// on the world seed and the chunk's own coordinates - never on
        /// when, or in what order, chunks happen to be visited.
        /// </summary>
        private List<ChunkPlacement> GeneratePlacements(ChunkCoord coord)
        {
            SimRandom random = _randomService.GetStream($"map.chunk_placements.{coord.X}.{coord.Z}");
            int cellsPerSide = (int)Math.Round(ChunkSizeMeters / CellSizeMeters);
            int treeCount = (int)_settings.Get(TreesPerChunkSettingId);
            int stoneCount = (int)_settings.Get(StonesPerChunkSettingId);
            int grubMoundCount = (int)_settings.Get(GrubMoundsPerChunkSettingId);

            var occupied = new HashSet<CellCoord>();
            var placements = new List<ChunkPlacement>();

            // Stones (the bigger footprint) first, while the chunk is still empty.
            PlaceKind(PlacementKind.Stone, stoneCount, FootprintCellsFor(PlacementKind.Stone), coord, cellsPerSide, random, occupied, placements);
            PlaceKind(PlacementKind.Tree, treeCount, FootprintCellsFor(PlacementKind.Tree), coord, cellsPerSide, random, occupied, placements);
            PlaceKind(PlacementKind.GrubMound, grubMoundCount, FootprintCellsFor(PlacementKind.GrubMound), coord, cellsPerSide, random, occupied, placements);

            return placements;
        }

        private static void PlaceKind(
            PlacementKind kind,
            int count,
            int footprintCells,
            ChunkCoord coord,
            int cellsPerSide,
            SimRandom random,
            HashSet<CellCoord> occupied,
            List<ChunkPlacement> placements)
        {
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
                {
                    int localX = random.NextInt(0, cellsPerSide - footprintCells + 1);
                    int localZ = random.NextInt(0, cellsPerSide - footprintCells + 1);

                    if (Overlaps(localX, localZ, footprintCells, occupied))
                    {
                        continue;
                    }

                    for (int dx = 0; dx < footprintCells; dx++)
                    {
                        for (int dz = 0; dz < footprintCells; dz++)
                        {
                            occupied.Add(new CellCoord(localX + dx, localZ + dz));
                        }
                    }

                    int worldCellX = coord.X * cellsPerSide + localX;
                    int worldCellZ = coord.Z * cellsPerSide + localZ;
                    placements.Add(new ChunkPlacement(kind, new CellCoord(worldCellX, worldCellZ)));
                    break;
                }
            }
        }

        private static bool Overlaps(int localX, int localZ, int footprintCells, HashSet<CellCoord> occupied)
        {
            for (int dx = 0; dx < footprintCells; dx++)
            {
                for (int dz = 0; dz < footprintCells; dz++)
                {
                    if (occupied.Contains(new CellCoord(localX + dx, localZ + dz)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Only ever-created chunks are written - an unvisited chunk stays lazy, generated fresh (and correctly) from its own never-yet-touched random stream whenever it's first needed.</summary>
        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_chunks.Count);
            foreach (KeyValuePair<ChunkCoord, ChunkData> entry in _chunks.OrderBy(kvp => kvp.Key.X).ThenBy(kvp => kvp.Key.Z))
            {
                writer.WriteInt(entry.Key.X);
                writer.WriteInt(entry.Key.Z);
                writer.WriteInt(entry.Value.Placements.Count);
                foreach (ChunkPlacement placement in entry.Value.Placements)
                {
                    writer.WriteInt((int)placement.Kind);
                    writer.WriteInt(placement.Cell.X);
                    writer.WriteInt(placement.Cell.Z);
                }
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int chunkCount = reader.ReadInt();
            for (int i = 0; i < chunkCount; i++)
            {
                var coord = new ChunkCoord(reader.ReadInt(), reader.ReadInt());
                int placementCount = reader.ReadInt();
                var placements = new List<ChunkPlacement>(placementCount);
                for (int p = 0; p < placementCount; p++)
                {
                    var kind = (PlacementKind)reader.ReadInt();
                    var cell = new CellCoord(reader.ReadInt(), reader.ReadInt());
                    placements.Add(new ChunkPlacement(kind, cell));
                }

                _chunks[coord] = new ChunkData(coord, placements);
            }
        }
    }
}
