using System;
using System.Collections.Generic;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.Buildings
{
    /// <summary>
    /// R11: placed buildings - for now, at most one, the keep, placed only
    /// once by the founding flow and never moved, added to or removed by
    /// the player. Depends on Map, to turn a world position into the
    /// footprint's cells.
    /// </summary>
    public sealed class BuildingsModule : IModule, ISaveableModule
    {
        public const string ModuleId = "module.buildings";
        public const string KeepContentId = "building.keep";

        /// <summary>R14: posted whenever a building is placed - feeds the discovery system.</summary>
        private const string BuildingPlacedEventId = "event.buildings.placed";

        private readonly MapModule _mapModule;
        private EntityRegistry _entities;
        private EntityTable<BuildingState> _buildings;
        private ModuleContext _context;

        public BuildingsModule(MapModule mapModule)
        {
            _mapModule = mapModule ?? throw new ArgumentNullException(nameof(mapModule));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public int SectionVersion => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapModule.ModuleId };

        /// <summary>True once a kingdom has been founded (at least one building placed) - the founding marker and popup only show while this is false.</summary>
        public bool IsFounded => _buildings.Count > 0;

        /// <summary>Every placed building's Id and state, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, BuildingState>> Buildings => _buildings.Rows();

        public bool TryGetBuilding(EntityId id, out BuildingState building) => _buildings.TryGet(id, out building);

        public void Initialize(ModuleContext context)
        {
            _entities = context.Entities;
            _context = context;
            _buildings = new EntityTable<BuildingState>(_entities);
        }

        public void Shutdown()
        {
        }

        /// <summary>
        /// Founding only: places a building centered on a world position,
        /// with its one door cell in the middle of its south edge. Never
        /// called for player input - the player can never place, move or
        /// remove a building.
        /// </summary>
        public EntityId PlaceBuilding(string contentId, double worldX, double worldZ, int footprintWidth, int footprintDepth)
        {
            CellCoord center = _mapModule.WorldToCell(worldX, worldZ);
            var minCell = new CellCoord(center.X - footprintWidth / 2, center.Z - footprintDepth / 2);
            var doorCell = new CellCoord(minCell.X + footprintWidth / 2, minCell.Z);

            var state = new BuildingState(contentId, worldX, worldZ, footprintWidth, footprintDepth, minCell, doorCell);
            EntityId id = _entities.Create(EntityKind.Building);
            _buildings.Add(id, state);

            _context.Events.Post(BuildingPlacedEventId, _context.CurrentTick, new EventData().SetString("contentId", contentId));

            return id;
        }

        /// <summary>Whether any building's footprint blocks this cell (every footprint cell except its door).</summary>
        public bool IsCellBlocked(CellCoord cell)
        {
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                if (row.Value.BlocksCell(cell))
                {
                    return true;
                }
            }

            return false;
        }

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_buildings.Count);
            foreach (KeyValuePair<EntityId, BuildingState> row in _buildings.Rows())
            {
                writer.WriteEntityId(row.Key);
                BuildingState state = row.Value;

                writer.WriteString(state.ContentId);
                writer.WriteDouble(state.WorldX);
                writer.WriteDouble(state.WorldZ);
                writer.WriteInt(state.FootprintWidth);
                writer.WriteInt(state.FootprintDepth);
                writer.WriteInt(state.MinCell.X);
                writer.WriteInt(state.MinCell.Z);
                writer.WriteInt(state.DoorCell.X);
                writer.WriteInt(state.DoorCell.Z);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                string contentId = reader.ReadString();
                double worldX = reader.ReadDouble();
                double worldZ = reader.ReadDouble();
                int footprintWidth = reader.ReadInt();
                int footprintDepth = reader.ReadInt();
                var minCell = new CellCoord(reader.ReadInt(), reader.ReadInt());
                var doorCell = new CellCoord(reader.ReadInt(), reader.ReadInt());

                _buildings.Add(id, new BuildingState(contentId, worldX, worldZ, footprintWidth, footprintDepth, minCell, doorCell));
            }
        }
    }
}
