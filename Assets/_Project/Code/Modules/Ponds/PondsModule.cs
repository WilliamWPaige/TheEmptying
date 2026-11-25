using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Core.Timekeeping;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.Ponds
{
    /// <summary>
    /// R17: ponds - a roughly round patch of water, 3 to 8 m across,
    /// scattered per chunk from the world seed exactly like MapModule's
    /// trees and stones, plus one guaranteed pond near the starting
    /// site. Unlike a tree or stone, each pond has real mutable state
    /// (how many drinks it currently holds, refilling over time), so
    /// it's a real entity with its own EntityTable row, not a stateless
    /// chunk placement. Depends on Map (chunk/cell coordinates and
    /// sizes, and the starting site's world position).
    /// </summary>
    public sealed class PondsModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.ponds";

        public const string PondsPerChunkSettingId = "setting.ponds.ponds_per_chunk";
        public const string PondMinRadiusMetersSettingId = "setting.ponds.min_radius_meters";
        public const string PondMaxRadiusMetersSettingId = "setting.ponds.max_radius_meters";
        public const string PondMaxDrinksSettingId = "setting.ponds.max_drinks";
        public const string PondRefillDrinksPerHourSettingId = "setting.ponds.refill_drinks_per_hour";
        public const string StartingPondMinDistanceMetersSettingId = "setting.ponds.starting_pond_min_distance_meters";
        public const string StartingPondMaxDistanceMetersSettingId = "setting.ponds.starting_pond_max_distance_meters";

        private const int MaxPlacementAttempts = 50;
        private const string ChunkStreamPrefix = "ponds.chunk_placements";
        private const string GuaranteedPondStreamName = "ponds.guaranteed_starting_pond";
        private const string RefillSystemId = "ponds.refill";

        private static readonly IReadOnlyList<EntityId> EmptyIds = Array.Empty<EntityId>();

        private readonly MapModule _mapModule;
        private readonly Dictionary<ChunkCoord, List<EntityId>> _pondsByChunk = new Dictionary<ChunkCoord, List<EntityId>>();
        private readonly HashSet<ChunkCoord> _generatedChunks = new HashSet<ChunkCoord>();

        private EntityRegistry _entities;
        private SettingsRegistry _settings;
        private RandomService _randomService;
        private ContentDatabase _content;
        private EntityTable<PondState> _ponds;

        public PondsModule(MapModule mapModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public int SectionVersion => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId };

        /// <summary>Every current pond's Id and state, in ascending entity index order - for Presentation and the info window.</summary>
        public IEnumerable<KeyValuePair<EntityId, PondState>> Ponds => _ponds.Rows();

        public bool TryGetPond(EntityId id, out PondState pond) => _ponds.TryGet(id, out pond);

        /// <summary>The live max-drinks setting value, for the info window.</summary>
        public double MaxDrinksSetting => _settings.Get(PondMaxDrinksSettingId);

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _settings = context.Settings;
            _randomService = context.RandomService;
            _content = context.Content;
            _ponds = new EntityTable<PondState>(_entities);

            context.RegisterSystem(new PondRefillSystem(this), interval: CalendarConstants.TicksPerHour, offset: 0, order: 0);

            if (!context.IsLoading)
            {
                CreateGuaranteedStartingPond();
            }
        }

        public void Shutdown()
        {
        }

        /// <summary>The ponds generated for this chunk, creating them (and marking the chunk generated) the first time it's asked for - mirrors MapModule.GetOrCreateChunk.</summary>
        public IReadOnlyList<EntityId> GetOrCreatePondsForChunk(ChunkCoord coord)
        {
            if (_generatedChunks.Contains(coord))
            {
                return _pondsByChunk.TryGetValue(coord, out List<EntityId> existing) ? existing : EmptyIds;
            }

            List<EntityId> created = GeneratePondsForChunk(coord);
            _generatedChunks.Add(coord);

            if (!_pondsByChunk.TryGetValue(coord, out List<EntityId> list))
            {
                list = new List<EntityId>();
                _pondsByChunk[coord] = list;
            }

            list.AddRange(created);
            return list;
        }

        /// <summary>Whether this cell is inside some pond's water - creating that chunk's ponds if it hasn't been asked for yet.</summary>
        public bool TryGetPondAt(CellCoord cell, out PondState pond)
        {
            double cellSize = _mapModule.CellSizeMeters;
            double worldX = (cell.X + 0.5) * cellSize;
            double worldZ = (cell.Z + 0.5) * cellSize;

            foreach (EntityId id in GetOrCreatePondsForChunk(_mapModule.CellToChunk(cell)))
            {
                if (_ponds.TryGet(id, out PondState candidate) && Distance(worldX, worldZ, candidate.CenterX, candidate.CenterZ) <= candidate.RadiusMeters)
                {
                    pond = candidate;
                    return true;
                }
            }

            pond = null;
            return false;
        }

        /// <summary>Takes one drink from the pond, never below 0 (dry).</summary>
        public void Drink(EntityId pondId)
        {
            if (_ponds.TryGet(pondId, out PondState pond))
            {
                pond.DrinksCurrent = Math.Max(0, pond.DrinksCurrent - 1);
            }
        }

        /// <summary>The "Empty pond"/"Fill pond" cheats - sets the pond's drinks directly, clamped to [0, max drinks].</summary>
        public void SetDrinks(EntityId pondId, int drinks)
        {
            if (_ponds.TryGet(pondId, out PondState pond))
            {
                int maxDrinks = (int)_settings.Get(PondMaxDrinksSettingId);
                pond.DrinksCurrent = Math.Clamp(drinks, 0, maxDrinks);
            }
        }

        /// <summary>
        /// R17: founding only - removes any pond whose water overlaps the
        /// given cell rectangle (min inclusive, max exclusive), the same
        /// as MapModule.RemovePlacementsOverlapping clears trees, stones
        /// and grub mounds. Generates the touched chunks' ponds first (if
        /// not already), so a pond that would have been generated there
        /// is correctly removed too, not silently skipped.
        /// </summary>
        public void RemovePondsOverlapping(CellCoord min, CellCoord maxExclusive)
        {
            double cellSize = _mapModule.CellSizeMeters;
            double minX = min.X * cellSize;
            double minZ = min.Z * cellSize;
            double maxX = maxExclusive.X * cellSize;
            double maxZ = maxExclusive.Z * cellSize;

            var touchedChunks = new HashSet<ChunkCoord>
            {
                _mapModule.CellToChunk(min),
                _mapModule.CellToChunk(new CellCoord(maxExclusive.X - 1, min.Z)),
                _mapModule.CellToChunk(new CellCoord(min.X, maxExclusive.Z - 1)),
                _mapModule.CellToChunk(new CellCoord(maxExclusive.X - 1, maxExclusive.Z - 1))
            };

            foreach (ChunkCoord coord in touchedChunks)
            {
                List<EntityId> ids = new List<EntityId>(GetOrCreatePondsForChunk(coord));
                foreach (EntityId id in ids)
                {
                    if (!_ponds.TryGet(id, out PondState pond))
                    {
                        continue;
                    }

                    bool overlaps = pond.CenterX + pond.RadiusMeters > minX && pond.CenterX - pond.RadiusMeters < maxX &&
                                     pond.CenterZ + pond.RadiusMeters > minZ && pond.CenterZ - pond.RadiusMeters < maxZ;

                    if (overlaps)
                    {
                        _ponds.Remove(id);
                        _pondsByChunk[coord].Remove(id);
                        _entities.Destroy(id);
                    }
                }
            }
        }

        private void CreateGuaranteedStartingPond()
        {
            List<StartingSiteDefinition> sites = BuiltInContentTypes.StartingSites(_content);
            if (sites.Count == 0)
            {
                return;
            }

            StartingSiteDefinition site = sites[0];
            double siteX = _mapModule.WorldWidthMeters * site.FractionX;
            double siteZ = _mapModule.WorldDepthMeters * site.FractionZ;

            SimRandom random = _randomService.GetStream(GuaranteedPondStreamName);
            double minDistance = _settings.Get(StartingPondMinDistanceMetersSettingId);
            double maxDistance = _settings.Get(StartingPondMaxDistanceMetersSettingId);
            double distance = minDistance + random.NextFloat() * (maxDistance - minDistance);
            double angle = random.NextFloat() * 2.0 * Math.PI;

            (double centerX, double centerZ) = _mapModule.ClampToWorld(siteX + Math.Cos(angle) * distance, siteZ + Math.Sin(angle) * distance);

            double minRadius = _settings.Get(PondMinRadiusMetersSettingId);
            double maxRadius = _settings.Get(PondMaxRadiusMetersSettingId);
            double radius = minRadius + random.NextFloat() * (maxRadius - minRadius);
            int maxDrinks = (int)_settings.Get(PondMaxDrinksSettingId);

            EntityId id = CreatePond(centerX, centerZ, radius, maxDrinks);

            // Added to the chunk's pond list WITHOUT marking that chunk "generated" - the normal per-chunk scatter still runs there later too, avoiding this one.
            ChunkCoord chunk = _mapModule.CellToChunk(_mapModule.WorldToCell(centerX, centerZ));
            if (!_pondsByChunk.TryGetValue(chunk, out List<EntityId> list))
            {
                list = new List<EntityId>();
                _pondsByChunk[chunk] = list;
            }

            list.Add(id);
        }

        /// <summary>
        /// TEMPORARY (R17), until the real map design exists: ponds
        /// scattered at random within this chunk's own bounds (never
        /// crossing into a neighbor, so a lookup only ever needs to check
        /// one chunk), avoiding every other pond already known here
        /// (including a guaranteed one). Uses a stream named for this
        /// exact chunk, so the result depends only on the world seed and
        /// the chunk's own coordinates - never on when, or in what order,
        /// chunks happen to be visited.
        /// </summary>
        private List<EntityId> GeneratePondsForChunk(ChunkCoord coord)
        {
            SimRandom random = _randomService.GetStream($"{ChunkStreamPrefix}.{coord.X}.{coord.Z}");
            double chunkSize = _mapModule.ChunkSizeMeters;
            (double originX, double originZ) = _mapModule.ChunkOrigin(coord);

            int pondCount = (int)_settings.Get(PondsPerChunkSettingId);
            double minRadius = _settings.Get(PondMinRadiusMetersSettingId);
            double maxRadius = _settings.Get(PondMaxRadiusMetersSettingId);
            int maxDrinks = (int)_settings.Get(PondMaxDrinksSettingId);

            var existing = new List<PondState>();
            if (_pondsByChunk.TryGetValue(coord, out List<EntityId> existingIds))
            {
                foreach (EntityId existingId in existingIds)
                {
                    if (_ponds.TryGet(existingId, out PondState existingPond))
                    {
                        existing.Add(existingPond);
                    }
                }
            }

            var created = new List<EntityId>();

            for (int i = 0; i < pondCount; i++)
            {
                for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
                {
                    double radius = minRadius + random.NextFloat() * (maxRadius - minRadius);
                    double span = chunkSize - 2 * radius;
                    if (span <= 0)
                    {
                        break;
                    }

                    double centerX = originX + radius + random.NextFloat() * span;
                    double centerZ = originZ + radius + random.NextFloat() * span;

                    if (OverlapsAny(centerX, centerZ, radius, existing))
                    {
                        continue;
                    }

                    EntityId id = CreatePond(centerX, centerZ, radius, maxDrinks);
                    created.Add(id);
                    existing.Add(_ponds.TryGet(id, out PondState newPond) ? newPond : null);
                    break;
                }
            }

            return created;
        }

        private static bool OverlapsAny(double centerX, double centerZ, double radius, List<PondState> existing)
        {
            foreach (PondState other in existing)
            {
                if (other != null && Distance(centerX, centerZ, other.CenterX, other.CenterZ) < radius + other.RadiusMeters)
                {
                    return true;
                }
            }

            return false;
        }

        private EntityId CreatePond(double centerX, double centerZ, double radiusMeters, int maxDrinks)
        {
            var state = new PondState { CenterX = centerX, CenterZ = centerZ, RadiusMeters = radiusMeters, DrinksCurrent = maxDrinks };
            EntityId id = _entities.Create(EntityKind.Pond);
            _ponds.Add(id, state);
            return id;
        }

        private void RefillPonds()
        {
            int maxDrinks = (int)_settings.Get(PondMaxDrinksSettingId);
            int refillPerHour = (int)_settings.Get(PondRefillDrinksPerHourSettingId);
            if (refillPerHour <= 0)
            {
                return;
            }

            foreach (KeyValuePair<EntityId, PondState> row in _ponds.Rows())
            {
                row.Value.DrinksCurrent = Math.Min(maxDrinks, row.Value.DrinksCurrent + refillPerHour);
            }
        }

        private static double Distance(double x1, double z1, double x2, double z2)
        {
            double dx = x1 - x2;
            double dz = z1 - z2;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        #region Save/load

        /// <summary>Only ever-generated chunks (and the guaranteed starting pond's chunk, generated or not) are written - an untouched chunk stays lazy, generated fresh from its own never-yet-touched random stream whenever it's first needed.</summary>
        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_ponds.Count);
            foreach (KeyValuePair<EntityId, PondState> row in _ponds.Rows())
            {
                writer.WriteEntityId(row.Key);
                writer.WriteDouble(row.Value.CenterX);
                writer.WriteDouble(row.Value.CenterZ);
                writer.WriteDouble(row.Value.RadiusMeters);
                writer.WriteInt(row.Value.DrinksCurrent);
            }

            writer.WriteInt(_pondsByChunk.Count);
            foreach (KeyValuePair<ChunkCoord, List<EntityId>> entry in _pondsByChunk.OrderBy(kvp => kvp.Key.X).ThenBy(kvp => kvp.Key.Z))
            {
                writer.WriteInt(entry.Key.X);
                writer.WriteInt(entry.Key.Z);
                writer.WriteBool(_generatedChunks.Contains(entry.Key));
                writer.WriteInt(entry.Value.Count);
                foreach (EntityId id in entry.Value)
                {
                    writer.WriteEntityId(id);
                }
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int pondCount = reader.ReadInt();
            for (int i = 0; i < pondCount; i++)
            {
                EntityId id = reader.ReadEntityId();
                var state = new PondState
                {
                    CenterX = reader.ReadDouble(),
                    CenterZ = reader.ReadDouble(),
                    RadiusMeters = reader.ReadDouble(),
                    DrinksCurrent = reader.ReadInt()
                };

                _ponds.Add(id, state);
            }

            int chunkCount = reader.ReadInt();
            for (int i = 0; i < chunkCount; i++)
            {
                var coord = new ChunkCoord(reader.ReadInt(), reader.ReadInt());
                bool generated = reader.ReadBool();
                int idCount = reader.ReadInt();
                var ids = new List<EntityId>(idCount);
                for (int j = 0; j < idCount; j++)
                {
                    ids.Add(reader.ReadEntityId());
                }

                _pondsByChunk[coord] = ids;
                if (generated)
                {
                    _generatedChunks.Add(coord);
                }
            }
        }

        #endregion

        /// <summary>The one scheduled system behind a pond's hourly refill - registered once per game hour, in ticks, so it always matches 1 game hour regardless of game speed.</summary>
        private sealed class PondRefillSystem : ISimSystem
        {
            private readonly PondsModule _module;

            public PondRefillSystem(PondsModule module)
            {
                _module = module;
            }

            public string Id => RefillSystemId;

            public void Update(long tick) => _module.RefillPonds();
        }
    }
}
