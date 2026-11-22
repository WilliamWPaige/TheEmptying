using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// People (R7): each person is an entity with a type and a world
    /// position. Nothing else yet - no names, needs, jobs or movement.
    /// Depends on Map, to find a free cell (not on a tree, stone or
    /// mound) to stand a person on.
    /// </summary>
    public sealed class PeopleModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.people";
        public const string FounderCountSettingId = "setting.people.founder_count";
        public const string PersonUnitContentId = "unit.person";

        /// <summary>Placeholder for "a few meters apart" - cells and meters are the same at the default 1 m cell size.</summary>
        private const int FounderSpacingCells = 3;

        private const int MaxSearchRadiusCells = 256;

        private readonly MapModule _mapModule;
        private EntityRegistry _entities;
        private SettingsRegistry _settings;
        private EntityTable<PersonState> _people;

        public PeopleModule(MapModule mapModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId };
        public int SectionVersion => 1;

        /// <summary>Every current person's Id and state, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, PersonState>> People => _people.Rows();

        public bool TryGetPerson(EntityId id, out PersonState person) => _people.TryGet(id, out person);

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _settings = context.Settings;
            _people = new EntityTable<PersonState>(_entities);

            if (!context.IsLoading)
            {
                SpawnFounders();
            }
        }

        public void Shutdown()
        {
        }

        /// <summary>Spawns a person on the nearest free cell to a world position (the Spawn cheat's "at the camera center"); null if none could be found.</summary>
        public EntityId? TrySpawnPersonNear(double worldX, double worldZ)
        {
            CellCoord center = _mapModule.WorldToCell(worldX, worldZ);
            List<CellCoord> cells = FindFreeCells(center, 1, minSpacingCells: 0);
            return cells.Count == 0 ? null : SpawnPersonAt(cells[0]);
        }

        public void RemovePerson(EntityId id)
        {
            if (_people.Has(id))
            {
                _entities.Destroy(id);
            }
        }

        private void SpawnFounders()
        {
            int count = (int)_settings.Get(FounderCountSettingId);
            CellCoord center = _mapModule.WorldToCell(_mapModule.WorldWidthMeters * 0.5, _mapModule.WorldDepthMeters * 0.5);

            foreach (CellCoord cell in FindFreeCells(center, count, FounderSpacingCells))
            {
                SpawnPersonAt(cell);
            }
        }

        private EntityId SpawnPersonAt(CellCoord cell)
        {
            EntityId id = _entities.Create(EntityKind.Person);
            double cellSize = _mapModule.CellSizeMeters;
            double worldX = (cell.X + 0.5) * cellSize;
            double worldZ = (cell.Z + 0.5) * cellSize;
            _people.Add(id, new PersonState(PersonUnitContentId, worldX, worldZ));
            return id;
        }

        /// <summary>
        /// Free cells (not on a tree, stone or mound), nearest to center
        /// first, at least minSpacingCells apart from each other - an
        /// expanding ring search, so the result is fully deterministic
        /// (no random rolls needed to place founders a few meters apart).
        /// </summary>
        private List<CellCoord> FindFreeCells(CellCoord center, int count, int minSpacingCells)
        {
            var accepted = new List<CellCoord>();

            for (int radius = 0; radius <= MaxSearchRadiusCells && accepted.Count < count; radius++)
            {
                foreach (CellCoord candidate in RingCells(center, radius))
                {
                    if (accepted.Count >= count)
                    {
                        break;
                    }

                    if (!_mapModule.IsCellFree(candidate))
                    {
                        continue;
                    }

                    if (IsTooCloseToAny(candidate, accepted, minSpacingCells))
                    {
                        continue;
                    }

                    accepted.Add(candidate);
                }
            }

            return accepted;
        }

        private static bool IsTooCloseToAny(CellCoord candidate, List<CellCoord> accepted, int minSpacingCells)
        {
            foreach (CellCoord other in accepted)
            {
                if (Math.Max(Math.Abs(other.X - candidate.X), Math.Abs(other.Z - candidate.Z)) < minSpacingCells)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The square ring of cells exactly radius cells from center (radius 0 is just center itself).</summary>
        private static IEnumerable<CellCoord> RingCells(CellCoord center, int radius)
        {
            if (radius == 0)
            {
                yield return center;
                yield break;
            }

            for (int dx = -radius; dx <= radius; dx++)
            {
                yield return new CellCoord(center.X + dx, center.Z - radius);
                yield return new CellCoord(center.X + dx, center.Z + radius);
            }

            for (int dz = -radius + 1; dz <= radius - 1; dz++)
            {
                yield return new CellCoord(center.X - radius, center.Z + dz);
                yield return new CellCoord(center.X + radius, center.Z + dz);
            }
        }

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_people.Count);
            foreach (KeyValuePair<EntityId, PersonState> row in _people.Rows())
            {
                writer.WriteEntityId(row.Key);
                writer.WriteString(row.Value.UnitContentId);
                writer.WriteDouble(row.Value.WorldX);
                writer.WriteDouble(row.Value.WorldZ);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                string unitContentId = reader.ReadString();
                double worldX = reader.ReadDouble();
                double worldZ = reader.ReadDouble();
                _people.Add(id, new PersonState(unitContentId, worldX, worldZ));
            }
        }
    }
}
