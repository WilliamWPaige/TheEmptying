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
using TheEmptying.Modules.Land;
using TheEmptying.Modules.MapMovement;

namespace TheEmptying.Modules.MapResources
{
    /// <summary>
    /// Trees and rock outcrops scattered near the keep at new-game start: a
    /// finite stock a woodcutter or quarrier (by hand or in a camp building)
    /// harvests until it runs out, at which point the node disappears.
    /// Passable, not an obstacle to movement or building placement - a
    /// gatherer has to stand on a node's own cell to work it (same as a hand
    /// gather spot always has), and the pathfinder can never route a mover
    /// onto a cell marked occupied, so marking a node's cell occupied (the
    /// other option the design allowed) would make every tree and outcrop
    /// permanently unreachable the instant it's placed. Depends on
    /// map/movement (the grid, for passability) and Land (the crown grounds
    /// rect, so nothing lands inside it).
    /// </summary>
    public sealed class MapResourcesModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.map_resources";

        public const string ForestCountSettingId = "setting.map_resources.forest_count";
        public const string TreesPerForestSettingId = "setting.map_resources.trees_per_forest";
        public const string OutcropCountSettingId = "setting.map_resources.outcrop_count";
        public const string LogsPerTreeSettingId = "setting.map_resources.logs_per_tree";
        public const string StonePerOutcropSettingId = "setting.map_resources.stone_per_outcrop";
        public const string PlacementRadiusCellsSettingId = "setting.map_resources.placement_radius_cells";
        public const string ForestClusterRadiusCellsSettingId = "setting.map_resources.forest_cluster_radius_cells";

        /// <summary>Posted when a node's stock reaches zero and it disappears - the presentation layer despawns its gray-box object.</summary>
        public const string NodeDepletedEventId = "event.map_resources.node_depleted";

        private readonly MapMovementModule _mapMovement;
        private readonly LandModule _land;

        private SettingsRegistry _settings;
        private SimRandom _random;
        private EventBus _events;
        private EntityRegistry _entities;
        private Func<long> _currentTick;
        private MapGrid _grid;

        private EntityTable<ResourceNodeState> _nodes;

        public MapResourcesModule(MapMovementModule mapMovement, LandModule land)
        {
            _mapMovement = mapMovement ?? throw new ArgumentNullException(nameof(mapMovement));
            _land = land ?? throw new ArgumentNullException(nameof(land));
        }

        public string Id => ModuleId;
        public int Version => 1;

        public IReadOnlyList<string> Dependencies { get; } = new[] { MapMovementModule.ModuleId, LandModule.ModuleId };

        public int SectionVersion => 1;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _random = context.Random;
            _events = context.Events;
            _entities = context.Entities;
            _currentTick = () => context.CurrentTick;
            _grid = _mapMovement.Grid;
            _nodes = new EntityTable<ResourceNodeState>(context.Entities);

            if (!context.IsLoading)
            {
                PlaceInitialResources();
            }

            RegisterCommands(context);
        }

        public void Shutdown()
        {
        }

        /// <summary>Every standing tree and outcrop, in ascending entity index order.</summary>
        public IEnumerable<KeyValuePair<EntityId, ResourceNodeState>> Nodes => _nodes.Rows();

        public bool TryGetNode(EntityId nodeId, out ResourceNodeState node) => _nodes.TryGet(nodeId, out node);

        #region New-game placement

        private void PlaceInitialResources()
        {
            int forestCount = (int)_settings.Get(ForestCountSettingId);
            int treesPerForest = (int)_settings.Get(TreesPerForestSettingId);
            int outcropCount = (int)_settings.Get(OutcropCountSettingId);
            int logsPerTree = (int)_settings.Get(LogsPerTreeSettingId);
            int stonePerOutcrop = (int)_settings.Get(StonePerOutcropSettingId);
            int radius = (int)_settings.Get(PlacementRadiusCellsSettingId);
            int clusterRadius = (int)_settings.Get(ForestClusterRadiusCellsSettingId);

            GridPoint anchor = _land.CrownGrounds.Center;
            var usedCells = new HashSet<GridPoint>();

            for (int f = 0; f < forestCount; f++)
            {
                GridPoint? clusterCenter = FindFreeCell(anchor, radius, usedCells);
                if (clusterCenter.HasValue)
                {
                    PlaceForest(clusterCenter.Value, clusterRadius, treesPerForest, logsPerTree, usedCells);
                }
            }

            for (int o = 0; o < outcropCount; o++)
            {
                GridPoint? cell = FindFreeCell(anchor, radius, usedCells);
                if (cell.HasValue)
                {
                    usedCells.Add(cell.Value);
                    CreateNode(ResourceNodeKind.Outcrop, cell.Value, stonePerOutcrop);
                }
            }
        }

        private void PlaceForest(GridPoint clusterCenter, int clusterRadius, int treeCount, int logsPerTree, HashSet<GridPoint> usedCells)
        {
            int placed = 0;
            int attempts = 0;
            int maxAttempts = treeCount * 20;

            while (placed < treeCount && attempts < maxAttempts)
            {
                attempts++;
                GridPoint? cell = FindFreeCell(clusterCenter, clusterRadius, usedCells);
                if (!cell.HasValue)
                {
                    continue;
                }

                usedCells.Add(cell.Value);
                CreateNode(ResourceNodeKind.Tree, cell.Value, logsPerTree);
                placed++;
            }
        }

        /// <summary>
        /// A passable cell within radius of anchor, outside the crown grounds and not already
        /// holding a resource node. Passable alone, not "unoccupied" too - a node is never an
        /// obstacle (see the class doc) - usedCells is this placement pass's own record of cells
        /// already spoken for, since nothing else would stop two nodes landing on the same cell.
        /// </summary>
        private GridPoint? FindFreeCell(GridPoint anchor, int radius, HashSet<GridPoint> usedCells)
        {
            const int attempts = 30;

            for (int i = 0; i < attempts; i++)
            {
                int dx = _random.NextInt(-radius, radius + 1);
                int dy = _random.NextInt(-radius, radius + 1);
                var candidate = new GridPoint(anchor.X + dx, anchor.Y + dy);

                if (_grid.IsPassable(candidate) && !_land.CrownGrounds.Contains(candidate) && !usedCells.Contains(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private EntityId CreateNode(ResourceNodeKind kind, GridPoint cell, int stock)
        {
            EntityId id = _entities.Create(EntityKind.Site);
            _nodes.Add(id, new ResourceNodeState { Kind = kind, Cell = cell, RemainingStock = stock });
            return id;
        }

        #endregion

        #region Finding and harvesting

        /// <summary>
        /// The nearest standing node of this kind, with stock remaining, not at a cell already
        /// claimed by another worker's task; false if none are in reach. "In reach" here just means
        /// "exists anywhere on the map" - callers that care about a maximum distance filter further.
        /// </summary>
        public bool TryFindNearest(ResourceNodeKind kind, GridPoint from, ICollection<GridPoint> excludingCells, out EntityId nodeId, out GridPoint cell)
        {
            nodeId = EntityId.None;
            cell = default;
            bool found = false;
            int bestDistance = int.MaxValue;

            foreach (KeyValuePair<EntityId, ResourceNodeState> row in _nodes.Rows())
            {
                if (row.Value.Kind != kind || row.Value.RemainingStock <= 0)
                {
                    continue;
                }

                if (excludingCells != null && excludingCells.Contains(row.Value.Cell))
                {
                    continue;
                }

                int distance = Math.Abs(from.X - row.Value.Cell.X) + Math.Abs(from.Y - row.Value.Cell.Y);
                if (!found || distance < bestDistance)
                {
                    found = true;
                    bestDistance = distance;
                    nodeId = row.Key;
                    cell = row.Value.Cell;
                }
            }

            return found;
        }

        /// <summary>
        /// Takes up to amount from this node; returns how much was actually taken (0 if the node no
        /// longer exists or already had none left). The node disappears - a NodeDepletedEventId
        /// posted - the instant its stock reaches zero.
        /// </summary>
        public int Harvest(EntityId nodeId, int amount)
        {
            if (amount <= 0 || !_nodes.TryGet(nodeId, out ResourceNodeState node) || node.RemainingStock <= 0)
            {
                return 0;
            }

            int taken = Math.Min(amount, node.RemainingStock);
            node.RemainingStock -= taken;

            if (node.RemainingStock <= 0)
            {
                _events.Post(NodeDepletedEventId, _currentTick(), new EventData().SetEntity("node", nodeId));
                _nodes.Remove(nodeId);
            }

            return taken;
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteInt(_nodes.Count);
            foreach (KeyValuePair<EntityId, ResourceNodeState> row in _nodes.Rows())
            {
                writer.WriteEntityId(row.Key);
                writer.WriteInt((int)row.Value.Kind);
                writer.WriteInt(row.Value.Cell.X);
                writer.WriteInt(row.Value.Cell.Y);
                writer.WriteInt(row.Value.RemainingStock);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var node = new ResourceNodeState
                {
                    Kind = (ResourceNodeKind)reader.ReadInt(),
                    Cell = new GridPoint(reader.ReadInt(), reader.ReadInt()),
                    RemainingStock = reader.ReadInt()
                };

                _nodes.Add(id, node);
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (!_nodes.TryGet(entity, out ResourceNodeState node))
            {
                yield break;
            }

            string kindName = node.Kind == ResourceNodeKind.Tree ? "tree" : "outcrop";
            string goodName = node.Kind == ResourceNodeKind.Tree ? "logs" : "stone";
            yield return $"map resource: {kindName} at ({node.Cell.X},{node.Cell.Y}), {node.RemainingStock} {goodName} left";
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("resources", "resources - lists every standing tree and outcrop and its remaining stock.", args =>
            {
                List<string> lines = _nodes.Rows().SelectMany(row => Inspect(row.Key)).ToList();
                return lines.Count == 0 ? "No trees or outcrops are standing." : string.Join("\n", lines);
            }));
        }

        #endregion
    }
}
