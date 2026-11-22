using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;

namespace TheEmptying.Modules.Map
{
    /// <summary>
    /// The world grid (R6): world size, cell size and chunk size (all
    /// settings, read live - never cached - since they can change between
    /// tests or a settings edit), and the per-chunk data store other
    /// systems ask "what is at this cell" through later. Chunks are
    /// created lazily, on first use, rather than filling a fixed array for
    /// the whole 15 km world - at 1 meter cells that would be 225 million
    /// entries for a world that is still just flat, empty ground.
    /// </summary>
    public sealed class MapModule : IModule
    {
        public const string WorldWidthSettingId = "setting.map.world_width_meters";
        public const string WorldDepthSettingId = "setting.map.world_depth_meters";
        public const string CellSizeSettingId = "setting.map.cell_size_meters";
        public const string ChunkSizeSettingId = "setting.map.chunk_size_meters";

        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new Dictionary<ChunkCoord, ChunkData>();

        private SettingsRegistry _settings;

        public string Id => "module.map";
        public int Version => 1;
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

        /// <summary>Creates the chunk's data the first time it's asked for; returns the same instance on every later call.</summary>
        public ChunkData GetOrCreateChunk(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out ChunkData chunk))
            {
                chunk = new ChunkData(coord);
                _chunks[coord] = chunk;
            }

            return chunk;
        }
    }
}
