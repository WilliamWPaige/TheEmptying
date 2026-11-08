using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Rules;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas
{
    /// <summary>
    /// All of the rule canvas's logic, with no UI code: positions, zoom, pan,
    /// selection, hit tests, and screen/canvas coordinate conversion. The UI
    /// (RuleCanvasView) only draws whatever this holds and forwards input to
    /// it, so every behavior here is testable without opening a window.
    ///
    /// Canvas space is the same coordinate system as RuleNode.X/Y. Screen
    /// space is wherever the viewport's pixels are. The transform is
    /// screen = canvas * Zoom + Pan.
    /// </summary>
    public sealed class RuleCanvasViewModel
    {
        public const float MinZoom = 0.25f;
        public const float MaxZoom = 2f;
        private const float FrameMargin = 40f;

        public RuleDefinition Rule { get; private set; }
        public BlockRegistry Blocks { get; private set; }

        public float Zoom { get; private set; } = 1f;
        public Vector2 Pan { get; private set; } = Vector2.zero;

        private readonly HashSet<string> _selectedNodeIds = new HashSet<string>();
        public IReadOnlyCollection<string> SelectedNodeIds => _selectedNodeIds;

        private readonly List<NodeViewModel> _nodes = new List<NodeViewModel>();
        private readonly Dictionary<string, NodeViewModel> _nodesById = new Dictionary<string, NodeViewModel>();
        public IReadOnlyList<NodeViewModel> Nodes => _nodes;

        private readonly List<WireViewModel> _wires = new List<WireViewModel>();
        public IReadOnlyList<WireViewModel> Wires => _wires;

        public RuleCanvasViewModel(RuleDefinition rule, BlockRegistry blocks)
        {
            Load(rule, blocks);
        }

        /// <summary>Loads a different rule (or a different block registry) and rebuilds every node and wire view. Resets selection, pan and zoom.</summary>
        public void Load(RuleDefinition rule, BlockRegistry blocks)
        {
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
            Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
            _selectedNodeIds.Clear();
            Zoom = 1f;
            Pan = Vector2.zero;
            Rebuild();
        }

        /// <summary>Rebuilds node and wire views from the current rule data and block registry. Call after structural changes (not needed after a plain drag, which only moves existing nodes).</summary>
        public void Rebuild()
        {
            _nodes.Clear();
            _nodesById.Clear();

            var blockTypesById = new Dictionary<string, BlockType>();
            foreach (RuleNode node in Rule.Nodes ?? Enumerable.Empty<RuleNode>())
            {
                Blocks.TryGet(node.BlockTypeId, out BlockType blockType);
                blockTypesById[node.Id] = blockType;
            }

            foreach (RuleNode node in Rule.Nodes ?? Enumerable.Empty<RuleNode>())
            {
                BlockType blockType = blockTypesById[node.Id];
                IReadOnlyList<PortViewModel> inferredPorts = blockType == null ? InferPorts(node.Id, blockTypesById) : null;
                var nodeView = new NodeViewModel(node, blockType, inferredPorts);
                _nodes.Add(nodeView);
                _nodesById[node.Id] = nodeView;
            }

            _wires.Clear();
            foreach (RuleConnection connection in Rule.Connections ?? Enumerable.Empty<RuleConnection>())
            {
                if (!_nodesById.TryGetValue(connection.FromNode, out NodeViewModel fromNode) ||
                    !_nodesById.TryGetValue(connection.ToNode, out NodeViewModel toNode))
                {
                    continue;
                }

                PortViewModel fromPort = fromNode.OutputPorts.FirstOrDefault(p => p.Name == connection.FromPort);
                PortViewModel toPort = toNode.InputPorts.FirstOrDefault(p => p.Name == connection.ToPort);
                if (fromPort == null || toPort == null)
                {
                    continue;
                }

                _wires.Add(new WireViewModel(connection, fromNode, fromPort, toNode, toPort));
            }
        }

        /// <summary>
        /// A missing-block node still draws its wires: guesses each connected
        /// port's name and direction straight from the rule's connections. A
        /// port's kind and data type come from the known block at the other
        /// end of its wire; only when both ends are missing does it fall back
        /// to an unknown (wildcard, visually distinct) data type.
        /// </summary>
        private List<PortViewModel> InferPorts(string nodeId, IReadOnlyDictionary<string, BlockType> blockTypesById)
        {
            var seen = new HashSet<(string Name, PortDirection Direction)>();
            var ports = new List<PortViewModel>();

            foreach (RuleConnection connection in Rule.Connections ?? Enumerable.Empty<RuleConnection>())
            {
                if (connection.FromNode == nodeId && seen.Add((connection.FromPort, PortDirection.Output)))
                {
                    (PortKind kind, RuleDataType? dataType) = ResolveNeighborPort(blockTypesById, connection.ToNode, connection.ToPort, PortDirection.Input);
                    ports.Add(new PortViewModel(connection.FromPort, PortDirection.Output, kind, dataType, isInferred: true));
                }

                if (connection.ToNode == nodeId && seen.Add((connection.ToPort, PortDirection.Input)))
                {
                    (PortKind kind, RuleDataType? dataType) = ResolveNeighborPort(blockTypesById, connection.FromNode, connection.FromPort, PortDirection.Output);
                    ports.Add(new PortViewModel(connection.ToPort, PortDirection.Input, kind, dataType, isInferred: true));
                }
            }

            return ports;
        }

        private static (PortKind Kind, RuleDataType? DataType) ResolveNeighborPort(IReadOnlyDictionary<string, BlockType> blockTypesById, string neighborNodeId, string neighborPortName, PortDirection neighborDirection)
        {
            if (blockTypesById.TryGetValue(neighborNodeId, out BlockType neighborBlockType) && neighborBlockType != null)
            {
                PortDefinition neighborPort = neighborBlockType.FindPort(neighborPortName, neighborDirection);
                if (neighborPort != null)
                {
                    return (neighborPort.Kind, neighborPort.DataType);
                }
            }

            return (PortKind.Data, null);
        }

        // --- Coordinates ---

        public Vector2 CanvasToScreen(Vector2 canvasPoint) => canvasPoint * Zoom + Pan;

        public Vector2 ScreenToCanvas(Vector2 screenPoint) => (screenPoint - Pan) / Zoom;

        // --- Pan and zoom ---

        public void PanBy(Vector2 screenDelta)
        {
            Pan += screenDelta;
        }

        /// <summary>Sets zoom to newZoom (clamped to [MinZoom, MaxZoom]), adjusting Pan so the canvas point currently under screenPoint stays under it.</summary>
        public void ZoomAt(Vector2 screenPoint, float newZoom)
        {
            Vector2 canvasPointUnderCursor = ScreenToCanvas(screenPoint);
            Zoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
            Pan = screenPoint - canvasPointUnderCursor * Zoom;
        }

        /// <summary>Multiplies the current zoom by factor (e.g. from a mouse wheel step), toward screenPoint.</summary>
        public void ZoomByFactor(Vector2 screenPoint, float factor)
        {
            ZoomAt(screenPoint, Zoom * factor);
        }

        // --- Framing ---

        public void FrameAll(Vector2 viewportSize) => FrameNodes(_nodes, viewportSize);

        /// <summary>No-op if nothing is selected.</summary>
        public void FrameSelected(Vector2 viewportSize)
        {
            List<NodeViewModel> selected = _nodes.Where(n => _selectedNodeIds.Contains(n.Node.Id)).ToList();
            if (selected.Count > 0)
            {
                FrameNodes(selected, viewportSize);
            }
        }

        private void FrameNodes(IReadOnlyList<NodeViewModel> nodes, Vector2 viewportSize)
        {
            if (nodes.Count == 0 || viewportSize.x <= 0f || viewportSize.y <= 0f)
            {
                return;
            }

            Rect bounds = nodes[0].Bounds;
            for (int i = 1; i < nodes.Count; i++)
            {
                bounds = Encapsulate(bounds, nodes[i].Bounds);
            }

            bounds = new Rect(bounds.x - FrameMargin, bounds.y - FrameMargin, bounds.width + FrameMargin * 2f, bounds.height + FrameMargin * 2f);

            float zoomToFitX = viewportSize.x / bounds.width;
            float zoomToFitY = viewportSize.y / bounds.height;
            Zoom = Mathf.Clamp(Mathf.Min(zoomToFitX, zoomToFitY), MinZoom, MaxZoom);

            Vector2 viewportCenter = viewportSize * 0.5f;
            Pan = viewportCenter - bounds.center * Zoom;
        }

        private static Rect Encapsulate(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        // --- Selection ---

        /// <summary>Click selection: picks the topmost node under canvasPoint. additive (Shift or Ctrl) toggles membership instead of replacing the selection; clicking empty space with additive false clears it.</summary>
        public void SelectAt(Vector2 canvasPoint, bool additive)
        {
            NodeViewModel hit = HitTestNode(canvasPoint);

            if (hit == null)
            {
                if (!additive)
                {
                    ClearSelection();
                }

                return;
            }

            if (additive)
            {
                if (!_selectedNodeIds.Add(hit.Node.Id))
                {
                    _selectedNodeIds.Remove(hit.Node.Id);
                }
            }
            else
            {
                _selectedNodeIds.Clear();
                _selectedNodeIds.Add(hit.Node.Id);
            }
        }

        /// <summary>Box selection: every node whose bounds overlap canvasRect. additive adds to the current selection instead of replacing it.</summary>
        public void SelectInBox(Rect canvasRect, bool additive)
        {
            if (!additive)
            {
                _selectedNodeIds.Clear();
            }

            foreach (NodeViewModel node in _nodes)
            {
                if (canvasRect.Overlaps(node.Bounds))
                {
                    _selectedNodeIds.Add(node.Node.Id);
                }
            }
        }

        public void ClearSelection()
        {
            _selectedNodeIds.Clear();
        }

        /// <summary>Selects a node directly by Id (no hit test) - e.g. after a paste, or jumping to a node from the problems panel. No-op if the Id doesn't exist. additive adds to the current selection instead of replacing it.</summary>
        public void SelectNodeById(string nodeId, bool additive)
        {
            if (!_nodesById.ContainsKey(nodeId))
            {
                return;
            }

            if (!additive)
            {
                _selectedNodeIds.Clear();
            }

            _selectedNodeIds.Add(nodeId);
        }

        /// <summary>The topmost (last-drawn) node whose bounds contain canvasPoint, or null.</summary>
        public NodeViewModel HitTestNode(Vector2 canvasPoint)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                if (_nodes[i].Bounds.Contains(canvasPoint))
                {
                    return _nodes[i];
                }
            }

            return null;
        }

        /// <summary>The closest port within radius canvas units of canvasPoint (its node and the port), or null. Used to start/end a wire drag and for Alt-click-to-disconnect.</summary>
        public (NodeViewModel Node, PortViewModel Port)? HitTestPort(Vector2 canvasPoint, float radius)
        {
            (NodeViewModel Node, PortViewModel Port)? best = null;
            float bestDistanceSquared = radius * radius;

            foreach (NodeViewModel node in _nodes)
            {
                foreach (PortViewModel port in node.Ports)
                {
                    float distanceSquared = (node.Position + port.LocalOffset - canvasPoint).sqrMagnitude;
                    if (distanceSquared <= bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        best = (node, port);
                    }
                }
            }

            return best;
        }

        /// <summary>The closest wire within radius canvas units of canvasPoint, approximated by distance to the straight segment between its two anchors (close enough for a gently-curved wire). Used to select and delete a wire.</summary>
        public WireViewModel HitTestWire(Vector2 canvasPoint, float radius)
        {
            WireViewModel best = null;
            float bestDistance = radius;

            foreach (WireViewModel wire in _wires)
            {
                float distance = DistancePointToSegment(canvasPoint, wire.FromAnchor, wire.ToAnchor);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = wire;
                }
            }

            return best;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < 0.0001f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            Vector2 projected = a + t * ab;
            return Vector2.Distance(point, projected);
        }

        // --- Dragging ---

        /// <summary>Moves every selected node by canvasDelta, writing the new position straight back to the rule data's RuleNode.X/Y. Nothing is written to disk.</summary>
        public void DragSelected(Vector2 canvasDelta)
        {
            foreach (string id in _selectedNodeIds)
            {
                if (_nodesById.TryGetValue(id, out NodeViewModel node))
                {
                    node.Node.X += canvasDelta.x;
                    node.Node.Y += canvasDelta.y;
                }
            }
        }
    }
}
