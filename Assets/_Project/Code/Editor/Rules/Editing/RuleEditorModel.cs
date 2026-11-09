using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules.Canvas;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>
    /// One rule's whole editing session: the mutable RuleDefinition, the
    /// read-only canvas view of it, its own undo/redo history (not Unity's
    /// Undo), and live validation. All UI (the window, the canvas view) only
    /// calls into this and reacts to Changed - every rule here is covered by
    /// an EditMode test.
    /// </summary>
    public sealed class RuleEditorModel
    {
        private sealed class Snapshot
        {
            public string RuleId;
            public string Name;
            public string Description;
            public bool Enabled;
            public List<RuleNode> Nodes;
            public List<RuleConnection> Connections;
        }

        public RuleDefinition Rule { get; }
        public EditorContext Context { get; private set; }
        public RuleCanvasViewModel Canvas { get; }
        public RuleCompileReport LastValidation { get; private set; }

        public bool IsDirty { get; private set; }
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>Raised after any change to the rule's data (structural or combined), after Canvas has been rebuilt and validation has run.</summary>
        public event Action Changed;

        private readonly List<Snapshot> _undoStack = new List<Snapshot>();
        private readonly List<Snapshot> _redoStack = new List<Snapshot>();
        private Snapshot _combineBeginSnapshot;
        private int _combineDepth;

        public RuleEditorModel(RuleDefinition rule, EditorContext context)
        {
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Rule.Nodes ??= new List<RuleNode>();
            Rule.Connections ??= new List<RuleConnection>();
            Canvas = new RuleCanvasViewModel(Rule, Context.Blocks);
            Validate();
        }

        /// <summary>Points this session at a freshly-built EditorContext (e.g. after a Refresh) without losing undo history or the current rule data.</summary>
        public void RebindContext(EditorContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Canvas.Load(Rule, Context.Blocks);
            Validate();
            Changed?.Invoke();
        }

        public void MarkSaved()
        {
            IsDirty = false;
        }

        // --- Node Ids ---

        /// <summary>A short, unique-within-this-rule Id: "n" plus one more than the highest existing "n&lt;number&gt;" Id, so a deleted node's old Id never resurfaces on a later, unrelated node.</summary>
        public string AllocateNodeId()
        {
            int max = 0;
            foreach (RuleNode node in Rule.Nodes)
            {
                if (node.Id != null && node.Id.Length > 1 && node.Id[0] == 'n' && int.TryParse(node.Id.Substring(1), out int number))
                {
                    max = Math.Max(max, number);
                }
            }

            return "n" + (max + 1);
        }

        // --- Structural edits (each is one undo step) ---

        public string AddNode(string blockTypeId, Vector2 canvasPosition)
        {
            PushUndoImmediate();
            string id = AddNodeInternal(blockTypeId, canvasPosition);
            CommitChange();
            return id;
        }

        /// <summary>
        /// Adds a node and, if existingNodeId/existingPortName names a real
        /// port on it, connects the new node to it too (figuring out which
        /// side is the output from existingPortDirection) - both in the same
        /// undo step. Used by the add-node menu's "drag a wire to empty
        /// canvas" flow, where the node and its first wire are one action
        /// from the user's point of view.
        /// </summary>
        public string AddNodeAndConnect(string blockTypeId, Vector2 canvasPosition, string existingNodeId, string existingPortName, PortDirection existingPortDirection, string newNodePortName)
        {
            PushUndoImmediate();

            string newNodeId = AddNodeInternal(blockTypeId, canvasPosition);
            Canvas.Rebuild(); // so the new node is resolvable by CanConnect/ConnectInternal below

            string fromNode = existingPortDirection == PortDirection.Output ? existingNodeId : newNodeId;
            string fromPort = existingPortDirection == PortDirection.Output ? existingPortName : newNodePortName;
            string toNode = existingPortDirection == PortDirection.Output ? newNodeId : existingNodeId;
            string toPort = existingPortDirection == PortDirection.Output ? newNodePortName : existingPortName;

            if (CanConnect(fromNode, fromPort, toNode, toPort).IsAllowed)
            {
                ConnectInternal(fromNode, fromPort, toNode, toPort);
            }

            CommitChange();
            return newNodeId;
        }

        private string AddNodeInternal(string blockTypeId, Vector2 canvasPosition)
        {
            string id = AllocateNodeId();
            Rule.Nodes.Add(new RuleNode
            {
                Id = id,
                BlockTypeId = blockTypeId,
                X = canvasPosition.x,
                Y = canvasPosition.y,
                Fields = new Dictionary<string, RuleFieldValueSource>()
            });

            return id;
        }

        public void DeleteNodes(IEnumerable<string> nodeIds)
        {
            var idSet = new HashSet<string>(nodeIds, StringComparer.Ordinal);
            if (idSet.Count == 0)
            {
                return;
            }

            PushUndoImmediate();

            Rule.Nodes.RemoveAll(n => idSet.Contains(n.Id));
            Rule.Connections.RemoveAll(c => idSet.Contains(c.FromNode) || idSet.Contains(c.ToNode));

            CommitChange();
        }

        public ConnectionCheckResult CanConnect(string fromNode, string fromPort, string toNode, string toPort)
        {
            if (fromNode == toNode)
            {
                return ConnectionCheckResult.Reject("Cannot connect a node to itself.");
            }

            NodeViewModel from = Canvas.Nodes.FirstOrDefault(n => n.Node.Id == fromNode);
            NodeViewModel to = Canvas.Nodes.FirstOrDefault(n => n.Node.Id == toNode);
            if (from == null || to == null)
            {
                return ConnectionCheckResult.Reject("Unknown node.");
            }

            PortViewModel fromPortView = from.OutputPorts.FirstOrDefault(p => p.Name == fromPort);
            PortViewModel toPortView = to.InputPorts.FirstOrDefault(p => p.Name == toPort);
            if (fromPortView == null)
            {
                return ConnectionCheckResult.Reject($"'{fromPort}' is not an output port on '{fromNode}'.");
            }

            if (toPortView == null)
            {
                return ConnectionCheckResult.Reject($"'{toPort}' is not an input port on '{toNode}'.");
            }

            if (fromPortView.Kind != toPortView.Kind)
            {
                return ConnectionCheckResult.Reject("Execution ports only connect to execution ports, and data ports only to data ports.");
            }

            if (fromPortView.Kind == PortKind.Data && fromPortView.DataType != null && toPortView.DataType != null && fromPortView.DataType != toPortView.DataType)
            {
                return ConnectionCheckResult.Reject($"Type mismatch: {fromPortView.DataType} does not match {toPortView.DataType}.");
            }

            return ConnectionCheckResult.Allow();
        }

        /// <summary>
        /// Connects fromPort to toPort if allowed, one undo step. Connecting
        /// to an input (data or execution) or an execution output that
        /// already has a wire replaces it; a data output may fan out to many.
        /// Returns null (no change) if the connection isn't allowed.
        /// </summary>
        public RuleConnection Connect(string fromNode, string fromPort, string toNode, string toPort)
        {
            if (!CanConnect(fromNode, fromPort, toNode, toPort).IsAllowed)
            {
                return null;
            }

            PushUndoImmediate();
            RuleConnection connection = ConnectInternal(fromNode, fromPort, toNode, toPort);
            CommitChange();
            return connection;
        }

        private RuleConnection ConnectInternal(string fromNode, string fromPort, string toNode, string toPort)
        {
            Rule.Connections.RemoveAll(c => c.ToNode == toNode && c.ToPort == toPort);

            NodeViewModel from = Canvas.Nodes.First(n => n.Node.Id == fromNode);
            PortViewModel fromPortView = from.OutputPorts.First(p => p.Name == fromPort);
            if (fromPortView.Kind == PortKind.Execution)
            {
                Rule.Connections.RemoveAll(c => c.FromNode == fromNode && c.FromPort == fromPort);
            }

            var connection = new RuleConnection { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };
            Rule.Connections.Add(connection);
            return connection;
        }

        public void Disconnect(RuleConnection connection)
        {
            if (connection == null || !Rule.Connections.Contains(connection))
            {
                return;
            }

            PushUndoImmediate();
            Rule.Connections.Remove(connection);
            CommitChange();
        }

        /// <summary>Alt-click a port: removes every wire attached to it, one undo step.</summary>
        public void DisconnectAllAtPort(string nodeId, string portName, PortDirection direction)
        {
            int before = Rule.Connections.Count;
            List<RuleConnection> remaining = direction == PortDirection.Input
                ? Rule.Connections.Where(c => !(c.ToNode == nodeId && c.ToPort == portName)).ToList()
                : Rule.Connections.Where(c => !(c.FromNode == nodeId && c.FromPort == portName)).ToList();

            if (remaining.Count == before)
            {
                return;
            }

            PushUndoImmediate();
            Rule.Connections.Clear();
            Rule.Connections.AddRange(remaining);
            CommitChange();
        }

        /// <summary>Swaps a node to a different block type (a family variant), dropping only the wires that no longer fit. One undo step.</summary>
        public VariantSwapResult SwapVariant(string nodeId, string newBlockTypeId)
        {
            RuleNode node = Rule.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null || !Context.Blocks.TryGet(newBlockTypeId, out BlockType newType))
            {
                return new VariantSwapResult(false, Array.Empty<RuleConnection>());
            }

            PushUndoImmediate();

            node.BlockTypeId = newBlockTypeId;

            var removed = new List<RuleConnection>();
            Rule.Connections.RemoveAll(c =>
            {
                bool touches = c.FromNode == nodeId || c.ToNode == nodeId;
                if (!touches)
                {
                    return false;
                }

                bool fits = ConnectionFitsAfterSwap(c, nodeId, newType);
                if (!fits)
                {
                    removed.Add(c);
                }

                return !fits;
            });

            CommitChange();
            return new VariantSwapResult(true, removed);
        }

        private bool ConnectionFitsAfterSwap(RuleConnection connection, string swappedNodeId, BlockType newType)
        {
            bool swappedIsFrom = connection.FromNode == swappedNodeId;
            string swappedPortName = swappedIsFrom ? connection.FromPort : connection.ToPort;
            PortDirection swappedDirection = swappedIsFrom ? PortDirection.Output : PortDirection.Input;

            PortDefinition newPort = newType.FindPort(swappedPortName, swappedDirection);
            if (newPort == null)
            {
                return false;
            }

            string otherNodeId = swappedIsFrom ? connection.ToNode : connection.FromNode;
            string otherPortName = swappedIsFrom ? connection.ToPort : connection.FromPort;
            PortDirection otherDirection = swappedIsFrom ? PortDirection.Input : PortDirection.Output;

            RuleNode otherNode = Rule.Nodes.FirstOrDefault(n => n.Id == otherNodeId);
            if (otherNode == null || !Context.Blocks.TryGet(otherNode.BlockTypeId, out BlockType otherType))
            {
                return true; // the other end is missing or unresolved; can't disprove the fit, so keep it
            }

            PortDefinition otherPort = otherType.FindPort(otherPortName, otherDirection);
            if (otherPort == null)
            {
                return false;
            }

            if (newPort.Kind != otherPort.Kind)
            {
                return false;
            }

            if (newPort.Kind == PortKind.Data && newPort.DataType != null && otherPort.DataType != null && newPort.DataType != otherPort.DataType)
            {
                return false;
            }

            return true;
        }

        // --- Combined edits (one undo step per session) ---

        public void BeginCombinedEdit()
        {
            if (_combineDepth == 0)
            {
                _combineBeginSnapshot = TakeSnapshot();
            }

            _combineDepth++;
        }

        public void EndCombinedEdit()
        {
            if (_combineDepth == 0)
            {
                return;
            }

            _combineDepth--;
            if (_combineDepth > 0)
            {
                return;
            }

            Snapshot before = _combineBeginSnapshot;
            _combineBeginSnapshot = null;

            if (before != null && !SnapshotEquals(before, TakeSnapshot()))
            {
                _undoStack.Add(before);
                _redoStack.Clear();
            }
        }

        /// <summary>Moves every selected node by canvasDelta. Call within BeginCombinedEdit/EndCombinedEdit so a whole drag is one undo step.</summary>
        public void DragSelected(Vector2 canvasDelta)
        {
            Canvas.DragSelected(canvasDelta);
            IsDirty = true;
            Changed?.Invoke();
        }

        /// <summary>Sets one field's value. Call within BeginCombinedEdit/EndCombinedEdit so a run of typing combines into one undo step.</summary>
        public void SetFieldValue(string nodeId, string fieldName, RuleFieldValueSource value)
        {
            RuleNode node = Rule.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
            {
                return;
            }

            node.Fields ??= new Dictionary<string, RuleFieldValueSource>();
            node.Fields[fieldName] = value;

            Canvas.Rebuild();
            Validate();
            IsDirty = true;
            Changed?.Invoke();
        }

        /// <summary>Sets the rule's own properties (Id, name, description, enabled). Call within BeginCombinedEdit/EndCombinedEdit while typing the Id/name/description.</summary>
        public void SetRuleProperties(string id, string name, string description, bool enabled)
        {
            Rule.Id = id;
            Rule.Name = name;
            Rule.Description = description;
            Rule.Enabled = enabled;

            Validate();
            IsDirty = true;
            Changed?.Invoke();
        }

        // --- Undo / redo ---

        public void Undo()
        {
            if (_undoStack.Count == 0)
            {
                return;
            }

            Snapshot current = TakeSnapshot();
            Snapshot previous = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(current);

            RestoreSnapshot(previous);
            AfterRestore();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0)
            {
                return;
            }

            Snapshot current = TakeSnapshot();
            Snapshot next = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(current);

            RestoreSnapshot(next);
            AfterRestore();
        }

        // --- Copy, paste, duplicate ---

        /// <summary>Serializes the selected nodes and the wires between them (both ends selected) as clipboard text - works across rules and files, since it carries everything needed with it.</summary>
        public string CopySelectionToClipboardText()
        {
            return CopyNodesToClipboardText(Canvas.SelectedNodeIds);
        }

        public string CopyNodesToClipboardText(IEnumerable<string> nodeIds)
        {
            var idSet = new HashSet<string>(nodeIds, StringComparer.Ordinal);
            List<RuleNode> nodes = Rule.Nodes.Where(n => idSet.Contains(n.Id)).ToList();
            List<RuleConnection> connections = Rule.Connections.Where(c => idSet.Contains(c.FromNode) && idSet.Contains(c.ToNode)).ToList();
            return RuleClipboard.Serialize(nodes, connections);
        }

        /// <summary>
        /// Pastes clipboard text (from this rule or any other) near
        /// pasteCanvasPosition, with fresh node Ids and the wires between
        /// pasted nodes kept (wires to nodes outside the copied set are
        /// already absent from the clipboard, since Copy only includes
        /// wires whose both ends were selected). Selects the new nodes.
        /// Returns the new node Ids, or empty if the text wasn't a valid payload.
        /// </summary>
        public IReadOnlyList<string> PasteFromClipboardText(string clipboardText, Vector2 pasteCanvasPosition)
        {
            if (!RuleClipboard.TryDeserialize(clipboardText, out List<RuleNode> copiedNodes, out List<RuleConnection> copiedConnections) || copiedNodes.Count == 0)
            {
                return Array.Empty<string>();
            }

            PushUndoImmediate();

            double minX = copiedNodes.Min(n => n.X);
            double minY = copiedNodes.Min(n => n.Y);

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var newNodes = new List<RuleNode>();

            foreach (RuleNode original in copiedNodes)
            {
                string newId = AllocateNodeId();
                idMap[original.Id] = newId;

                newNodes.Add(new RuleNode
                {
                    Id = newId,
                    BlockTypeId = original.BlockTypeId,
                    X = pasteCanvasPosition.x + (original.X - minX),
                    Y = pasteCanvasPosition.y + (original.Y - minY),
                    Fields = CloneFields(original.Fields)
                });

                // Reserve the Id immediately so the next AllocateNodeId call in this loop doesn't reuse it.
                Rule.Nodes.Add(newNodes[^1]);
            }

            foreach (RuleConnection original in copiedConnections)
            {
                if (idMap.TryGetValue(original.FromNode, out string newFrom) && idMap.TryGetValue(original.ToNode, out string newTo))
                {
                    Rule.Connections.Add(new RuleConnection { FromNode = newFrom, FromPort = original.FromPort, ToNode = newTo, ToPort = original.ToPort });
                }
            }

            CommitChange();

            Canvas.ClearSelection();
            foreach (string id in idMap.Values)
            {
                Canvas.SelectNodeById(id, additive: true);
            }

            return idMap.Values.ToList();
        }

        /// <summary>Copy then paste in one step, offset slightly so the duplicate doesn't sit exactly on top of the original.</summary>
        public IReadOnlyList<string> DuplicateSelection()
        {
            if (Canvas.SelectedNodeIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            string clipboardText = CopySelectionToClipboardText();
            NodeViewModel first = Canvas.Nodes.First(n => Canvas.SelectedNodeIds.Contains(n.Node.Id));
            Vector2 pastePosition = first.Position + new Vector2(40f, 40f);
            return PasteFromClipboardText(clipboardText, pastePosition);
        }

        // --- Validation ---

        public void Validate()
        {
            var validationRule = new RuleDefinition
            {
                Id = Rule.Id,
                Name = Rule.Name,
                Description = Rule.Description,
                Enabled = true, // validate regardless of the rule's own enabled flag
                Nodes = Rule.Nodes,
                Connections = Rule.Connections
            };

            var database = new ContentDatabase();
            database.Set(BuiltInContentTypes.RuleTypeName, validationRule, "editor", "in-memory");

            (_, RuleCompileReport report) = RuleCompiler.Compile(database, Context.Blocks, Context.Settings, Context.Events, ErrorMode.Release);
            LastValidation = report;
        }

        public IReadOnlyCollection<string> NodeIdsWithErrors =>
            LastValidation == null ? Array.Empty<string>() : LastValidation.Errors.Where(p => p.NodeId != null).Select(p => p.NodeId).Distinct().ToList();

        public IReadOnlyCollection<string> NodeIdsWithWarnings =>
            LastValidation == null ? Array.Empty<string>() : LastValidation.Warnings.Where(p => p.NodeId != null).Select(p => p.NodeId).Distinct().ToList();

        // --- Serialization (this rule only; the window owns the file-level list) ---

        public string SerializeRuleOnly() => RuleJsonSerializer.SerializeFile(new List<RuleDefinition> { Rule });

        // --- Internals ---

        private static Dictionary<string, RuleFieldValueSource> CloneFields(Dictionary<string, RuleFieldValueSource> fields)
        {
            return fields == null
                ? new Dictionary<string, RuleFieldValueSource>()
                : fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        }

        private void PushUndoImmediate()
        {
            _undoStack.Add(TakeSnapshot());
            _redoStack.Clear();
        }

        private void CommitChange()
        {
            Canvas.Rebuild();
            Validate();
            IsDirty = true;
            Changed?.Invoke();
        }

        private void AfterRestore()
        {
            Canvas.Rebuild();
            Validate();
            IsDirty = true;
            Changed?.Invoke();
        }

        private Snapshot TakeSnapshot()
        {
            return new Snapshot
            {
                RuleId = Rule.Id,
                Name = Rule.Name,
                Description = Rule.Description,
                Enabled = Rule.Enabled,
                Nodes = Rule.Nodes.Select(CloneNode).ToList(),
                Connections = Rule.Connections.Select(CloneConnection).ToList()
            };
        }

        private void RestoreSnapshot(Snapshot snapshot)
        {
            Rule.Id = snapshot.RuleId;
            Rule.Name = snapshot.Name;
            Rule.Description = snapshot.Description;
            Rule.Enabled = snapshot.Enabled;
            Rule.Nodes = snapshot.Nodes.Select(CloneNode).ToList();
            Rule.Connections = snapshot.Connections.Select(CloneConnection).ToList();
        }

        private static RuleNode CloneNode(RuleNode node)
        {
            return new RuleNode
            {
                Id = node.Id,
                BlockTypeId = node.BlockTypeId,
                X = node.X,
                Y = node.Y,
                Fields = CloneFields(node.Fields)
            };
        }

        private static RuleConnection CloneConnection(RuleConnection connection)
        {
            return new RuleConnection { FromNode = connection.FromNode, FromPort = connection.FromPort, ToNode = connection.ToNode, ToPort = connection.ToPort };
        }

        private static bool SnapshotEquals(Snapshot a, Snapshot b)
        {
            if (a.RuleId != b.RuleId || a.Name != b.Name || a.Description != b.Description || a.Enabled != b.Enabled)
            {
                return false;
            }

            if (a.Nodes.Count != b.Nodes.Count || a.Connections.Count != b.Connections.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Nodes.Count; i++)
            {
                if (!NodeEquals(a.Nodes[i], b.Nodes[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < a.Connections.Count; i++)
            {
                if (!ConnectionEquals(a.Connections[i], b.Connections[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NodeEquals(RuleNode x, RuleNode y)
        {
            if (x.Id != y.Id || x.BlockTypeId != y.BlockTypeId || x.X != y.X || x.Y != y.Y)
            {
                return false;
            }

            Dictionary<string, RuleFieldValueSource> xf = x.Fields ?? new Dictionary<string, RuleFieldValueSource>();
            Dictionary<string, RuleFieldValueSource> yf = y.Fields ?? new Dictionary<string, RuleFieldValueSource>();
            if (xf.Count != yf.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, RuleFieldValueSource> kvp in xf)
            {
                if (!yf.TryGetValue(kvp.Key, out RuleFieldValueSource otherValue) || !FieldValueEquals(kvp.Value, otherValue))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FieldValueEquals(RuleFieldValueSource a, RuleFieldValueSource b)
        {
            if (a == null && b == null)
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (a.IsSettingReference != b.IsSettingReference)
            {
                return false;
            }

            return a.IsSettingReference ? a.SettingId == b.SettingId : JToken.DeepEquals(a.Literal, b.Literal);
        }

        private static bool ConnectionEquals(RuleConnection x, RuleConnection y) =>
            x.FromNode == y.FromNode && x.FromPort == y.FromPort && x.ToNode == y.ToNode && x.ToPort == y.ToPort;
    }
}
