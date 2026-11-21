using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Modules.MapMovement.Pathfinding;

namespace TheEmptying.Modules.MapMovement
{
    /// <summary>
    /// Owns the flat grid, pathfinding and everything that moves. Any entity
    /// can be made a mover; nothing here assumes a person.
    /// </summary>
    public sealed class MapMovementModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.map_movement";

        public const string CellSizeMetersSettingId = "setting.map_movement.cell_size_meters";
        public const string MapWidthCellsSettingId = "setting.map_movement.map_width_cells";
        public const string MapDepthCellsSettingId = "setting.map_movement.map_depth_cells";
        public const string MaxPathSearchesPerTickSettingId = "setting.map_movement.max_path_searches_per_tick";
        public const string WalkingSpeedMetersPerTickSettingId = "setting.map_movement.walking_speed_meters_per_tick";

        public const string MoverArrivedEventId = "event.map_movement.mover_arrived";
        public const string MoverNoPathEventId = "event.map_movement.mover_no_path";

        private sealed class PathRequestSystem : ISimSystem
        {
            private readonly MapMovementModule _owner;
            public PathRequestSystem(MapMovementModule owner) => _owner = owner;
            public string Id => ModuleId + ".path_requests";
            public void Update(long tick) => _owner.ProcessPathRequests(tick);
        }

        private sealed class MovementSystem : ISimSystem
        {
            private readonly MapMovementModule _owner;
            public MovementSystem(MapMovementModule owner) => _owner = owner;
            public string Id => ModuleId + ".movement";
            public void Update(long tick) => _owner.AdvanceMovers(tick);
        }

        private MapGrid _grid;
        private EntityTable<MoverState> _movers;
        private readonly Queue<PathRequest> _requestQueue = new Queue<PathRequest>();
        private readonly AStarPathfinder _pathfinder = new AStarPathfinder();
        private SettingsRegistry _settings;
        private EventBus _events;
        private EntityRegistry _entities;
        private SimRandom _random;

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();
        public int SectionVersion => 1;

        /// <summary>The grid, for the presentation layer (ground rendering) and tests.</summary>
        public MapGrid Grid => _grid;

        /// <summary>Every current mover's Id and state, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, MoverState>> Movers => _movers.Rows();

        public int PendingRequestCount => _requestQueue.Count;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _entities = context.Entities;
            _random = context.Random;

            // Map dimensions and cell size are structural (the grid is allocated
            // once here), unlike a difficulty slider - re-reading them mid-game
            // would require regenerating the whole grid, which nothing does.
            int width = (int)_settings.Get(MapWidthCellsSettingId);
            int depth = (int)_settings.Get(MapDepthCellsSettingId);
            double cellSize = _settings.Get(CellSizeMetersSettingId);
            _grid = new MapGrid(width, depth, cellSize);

            _movers = new EntityTable<MoverState>(context.Entities);

            context.RegisterSystem(new PathRequestSystem(this), 1, 0, 0);
            context.RegisterSystem(new MovementSystem(this), 1, 0, 1);

            RegisterCommands(context);
        }

        public void Shutdown()
        {
        }

        #region Public API

        /// <summary>Creates a new entity and makes it a mover at a random passable cell. Returns EntityId.None if the map has no passable cell at all.</summary>
        public EntityId SpawnMover(EntityKind kind = EntityKind.Person)
        {
            GridPoint? cell = _grid.RandomPassableCell(_random);
            if (cell == null)
            {
                return EntityId.None;
            }

            EntityId id = _entities.Create(kind);
            var state = new MoverState();
            (state.X, state.Y) = _grid.CellCenterWorldPosition(cell.Value);
            _movers.Add(id, state);
            return id;
        }

        /// <summary>Makes an already-existing entity a mover at the given cell (for modules, such as people, that create their own entities). False if it's already a mover or the cell isn't passable.</summary>
        public bool MakeMover(EntityId entity, GridPoint atCell)
        {
            if (_movers.Has(entity) || !_grid.IsPassable(atCell))
            {
                return false;
            }

            var state = new MoverState();
            (state.X, state.Y) = _grid.CellCenterWorldPosition(atCell);
            _movers.Add(entity, state);
            return true;
        }

        public bool IsMover(EntityId entity) => _movers.Has(entity);

        /// <summary>This mover's current destination cell, if it's walking anywhere right now.</summary>
        public GridPoint? GetDestination(EntityId entity) => _movers.TryGet(entity, out MoverState state) ? state.Destination : null;

        /// <summary>The mover's current cell. Throws if entity isn't a mover.</summary>
        public GridPoint GetCell(EntityId entity)
        {
            MoverState state = _movers.Get(entity);
            return _grid.WorldToCell(state.X, state.Y);
        }

        public bool TryGetPosition(EntityId entity, out double x, out double y)
        {
            if (_movers.TryGet(entity, out MoverState state))
            {
                x = state.X;
                y = state.Y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        /// <summary>Cancels any path, destination and pending request for a mover, leaving it standing where it is. No-op if entity isn't a mover.</summary>
        public void Stop(EntityId entity)
        {
            if (!_movers.TryGet(entity, out MoverState state))
            {
                return;
            }

            state.Path.Clear();
            state.Destination = null;
            state.HasPendingRequest = false;
        }

        /// <summary>Queues a path request from the mover's current cell to destination. False if entity isn't a mover.</summary>
        public bool TryMoveTo(EntityId entity, GridPoint destination)
        {
            if (!_movers.TryGet(entity, out MoverState state))
            {
                return false;
            }

            state.Destination = destination;
            state.HasPendingRequest = true;
            state.Path.Clear();

            GridPoint start = _grid.WorldToCell(state.X, state.Y);
            _requestQueue.Enqueue(new PathRequest(entity, start, destination));
            return true;
        }

        /// <summary>Sends an existing mover toward a uniformly random passable cell. False if entity isn't a mover or no passable cell exists.</summary>
        public bool TryMoveToRandomDestination(EntityId entity)
        {
            GridPoint? cell = _grid.RandomPassableCell(_random);
            return cell != null && TryMoveTo(entity, cell.Value);
        }

        #endregion

        #region Systems

        private void ProcessPathRequests(long tick)
        {
            int maxSearches = (int)_settings.Get(MaxPathSearchesPerTickSettingId);

            for (int i = 0; i < maxSearches && _requestQueue.Count > 0; i++)
            {
                PathRequest request = _requestQueue.Dequeue();

                if (!_movers.TryGet(request.Mover, out MoverState state))
                {
                    continue; // the mover no longer exists
                }

                // A newer request for the same mover may already have replaced
                // this one's destination; a stale result is simply dropped.
                if (state.Destination == null || state.Destination.Value != request.Goal)
                {
                    continue;
                }

                PathResult result = _pathfinder.FindPath(_grid, request.Start, request.Goal);

                if (!result.Found)
                {
                    state.HasPendingRequest = false;
                    state.Destination = null;
                    _events.Post(MoverNoPathEventId, tick, new EventData()
                        .SetEntity("mover", request.Mover)
                        .SetLong("goalX", request.Goal.X)
                        .SetLong("goalY", request.Goal.Y));
                    continue;
                }

                state.HasPendingRequest = false;
                state.Path.Clear();

                // Cells[0] is the mover's own current cell; nothing to walk to get there.
                for (int c = 1; c < result.Cells.Count; c++)
                {
                    state.Path.Enqueue(result.Cells[c]);
                }

                if (state.Path.Count == 0)
                {
                    // Start and goal were the same cell: arrived immediately.
                    state.Destination = null;
                    _events.Post(MoverArrivedEventId, tick, new EventData().SetEntity("mover", request.Mover));
                }
            }
        }

        private void AdvanceMovers(long tick)
        {
            double metersPerTick = _settings.Get(WalkingSpeedMetersPerTickSettingId);

            foreach (KeyValuePair<EntityId, MoverState> row in _movers.Rows())
            {
                MoverState state = row.Value;
                if (state.Path.Count == 0)
                {
                    continue;
                }

                GridPoint nextCell = state.Path.Peek();
                if (!_grid.IsPassable(nextCell))
                {
                    // The path ahead was blocked after it was planned; ask for a new one.
                    state.Path.Clear();
                    if (state.Destination != null)
                    {
                        GridPoint start = _grid.WorldToCell(state.X, state.Y);
                        state.HasPendingRequest = true;
                        _requestQueue.Enqueue(new PathRequest(row.Key, start, state.Destination.Value));
                    }

                    continue;
                }

                double remaining = metersPerTick;
                while (remaining > 0.0 && state.Path.Count > 0)
                {
                    GridPoint target = state.Path.Peek();
                    if (!_grid.IsPassable(target))
                    {
                        break; // re-checked next tick, above
                    }

                    (double targetX, double targetY) = _grid.CellCenterWorldPosition(target);
                    double dx = targetX - state.X;
                    double dy = targetY - state.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance <= remaining || distance <= 1e-9)
                    {
                        state.X = targetX;
                        state.Y = targetY;
                        remaining -= distance;
                        state.Path.Dequeue();
                    }
                    else
                    {
                        double t = remaining / distance;
                        state.X += dx * t;
                        state.Y += dy * t;
                        remaining = 0.0;
                    }
                }

                if (state.Path.Count == 0 && state.Destination != null)
                {
                    state.Destination = null;
                    _events.Post(MoverArrivedEventId, tick, new EventData().SetEntity("mover", row.Key));
                }
            }
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_movers.Count);
            foreach (KeyValuePair<EntityId, MoverState> row in _movers.Rows())
            {
                MoverState state = row.Value;
                writer.WriteEntityId(row.Key);
                writer.WriteDouble(state.X);
                writer.WriteDouble(state.Y);

                writer.WriteBool(state.Destination != null);
                if (state.Destination != null)
                {
                    writer.WriteInt(state.Destination.Value.X);
                    writer.WriteInt(state.Destination.Value.Y);
                }

                writer.WriteBool(state.HasPendingRequest);

                writer.WriteInt(state.Path.Count);
                foreach (GridPoint cell in state.Path)
                {
                    writer.WriteInt(cell.X);
                    writer.WriteInt(cell.Y);
                }
            }

            writer.WriteInt(_requestQueue.Count);
            foreach (PathRequest request in _requestQueue)
            {
                writer.WriteEntityId(request.Mover);
                writer.WriteInt(request.Start.X);
                writer.WriteInt(request.Start.Y);
                writer.WriteInt(request.Goal.X);
                writer.WriteInt(request.Goal.Y);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int moverCount = reader.ReadInt();
            for (int i = 0; i < moverCount; i++)
            {
                EntityId id = reader.ReadEntityId();
                var state = new MoverState
                {
                    X = reader.ReadDouble(),
                    Y = reader.ReadDouble()
                };

                if (reader.ReadBool())
                {
                    state.Destination = new GridPoint(reader.ReadInt(), reader.ReadInt());
                }

                state.HasPendingRequest = reader.ReadBool();

                int pathCount = reader.ReadInt();
                for (int p = 0; p < pathCount; p++)
                {
                    state.Path.Enqueue(new GridPoint(reader.ReadInt(), reader.ReadInt()));
                }

                _movers.Add(id, state);
            }

            _requestQueue.Clear();
            int requestCount = reader.ReadInt();
            for (int i = 0; i < requestCount; i++)
            {
                EntityId mover = reader.ReadEntityId();
                var start = new GridPoint(reader.ReadInt(), reader.ReadInt());
                var goal = new GridPoint(reader.ReadInt(), reader.ReadInt());
                _requestQueue.Enqueue(new PathRequest(mover, start, goal));
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (!_movers.TryGet(entity, out MoverState state))
            {
                yield break;
            }

            GridPoint cell = _grid.WorldToCell(state.X, state.Y);
            yield return $"position: ({state.X:F1}, {state.Y:F1}) m, cell {cell}";
            yield return state.Destination != null
                ? $"destination: {state.Destination.Value}, {state.Path.Count} cell(s) remaining, pending: {state.HasPendingRequest}"
                : "destination: none";
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("spawnmover", "spawnmover <count> - spawns <count> movers at random free cells.", args =>
            {
                if (args.Length != 1 || !ConsoleArgs.TryParseInt(args[0], out int count) || count <= 0)
                {
                    return "Usage: spawnmover <count>";
                }

                var ids = new List<EntityId>(count);
                for (int i = 0; i < count; i++)
                {
                    EntityId id = SpawnMover();
                    if (id.IsNone)
                    {
                        break;
                    }

                    ids.Add(id);
                }

                return ids.Count == count
                    ? $"Spawned {ids.Count} mover(s): {string.Join(", ", ids)}"
                    : $"Spawned {ids.Count} of {count} requested (no more free cells): {string.Join(", ", ids)}";
            }));

            context.RegisterCommand(new ConsoleCommand("moveto", "moveto <entityId> <x> <y> - sends a mover toward grid cell (x, y).", args =>
            {
                if (args.Length != 3 || !EntityId.TryParse(args[0], out EntityId id) ||
                    !ConsoleArgs.TryParseInt(args[1], out int x) || !ConsoleArgs.TryParseInt(args[2], out int y))
                {
                    return "Usage: moveto <entityId> <x> <y>";
                }

                if (!IsMover(id))
                {
                    return $"{id} is not a mover (use spawnmover first).";
                }

                var destination = new GridPoint(x, y);
                if (!_grid.InBounds(destination))
                {
                    return $"({x}, {y}) is outside the map ({_grid.Width} x {_grid.Depth}).";
                }

                TryMoveTo(id, destination);
                return $"{id} moving toward {destination}.";
            }));

            context.RegisterCommand(new ConsoleCommand("moverandom", "moverandom <entityId|all> - sends one or every mover toward a random free cell.", args =>
            {
                if (args.Length != 1)
                {
                    return "Usage: moverandom <entityId|all>";
                }

                if (string.Equals(args[0], "all", StringComparison.OrdinalIgnoreCase))
                {
                    int sent = 0;
                    foreach (KeyValuePair<EntityId, MoverState> row in _movers.Rows().ToList())
                    {
                        if (TryMoveToRandomDestination(row.Key))
                        {
                            sent++;
                        }
                    }

                    return $"Sent {sent} mover(s) toward a random destination.";
                }

                if (!EntityId.TryParse(args[0], out EntityId id) || !IsMover(id))
                {
                    return "Usage: moverandom <entityId|all>";
                }

                return TryMoveToRandomDestination(id)
                    ? $"{id} moving toward a random destination."
                    : "No free cell was found.";
            }));
        }

        #endregion
    }
}
