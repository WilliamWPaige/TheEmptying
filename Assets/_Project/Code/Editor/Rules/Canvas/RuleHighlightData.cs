using System.Collections.Generic;
using TheEmptying.Core.Rules.Tracing;

namespace TheEmptying.Editor.Rules.Canvas
{
    /// <summary>
    /// Pure, UI-free readout of what a trace segment's run should highlight
    /// on the canvas: each node's 1-based position in execution order, which
    /// execution outputs were followed (so their wires can be drawn
    /// highlighted), the value read or computed at each node, and which node
    /// (if any) an error happened on. RuleCanvasView only applies this data
    /// to NodeElement and WiresLayer; every bit of logic for deriving it
    /// lives here, so it can be unit tested without opening a window.
    /// </summary>
    public sealed class RuleHighlightData
    {
        public IReadOnlyDictionary<string, int> ExecutionOrderByNode { get; }
        public HashSet<(string FromNode, string FromPort)> FollowedExecutionOutputs { get; }
        public IReadOnlyDictionary<string, List<(string Port, string Value)>> InputValuesByNode { get; }
        public IReadOnlyDictionary<string, string> ComputedValueByNode { get; }
        public string ErrorNodeId { get; }
        public string ErrorMessage { get; }

        private RuleHighlightData(
            Dictionary<string, int> executionOrderByNode,
            HashSet<(string, string)> followedExecutionOutputs,
            Dictionary<string, List<(string, string)>> inputValuesByNode,
            Dictionary<string, string> computedValueByNode,
            string errorNodeId,
            string errorMessage)
        {
            ExecutionOrderByNode = executionOrderByNode;
            FollowedExecutionOutputs = followedExecutionOutputs;
            InputValuesByNode = inputValuesByNode;
            ComputedValueByNode = computedValueByNode;
            ErrorNodeId = errorNodeId;
            ErrorMessage = errorMessage;
        }

        /// <summary>Walks one trace segment's entries once, in order, building every piece of highlight data a run needs. Null segment yields empty, unhighlighted data.</summary>
        public static RuleHighlightData Build(RuleTraceSegment segment)
        {
            var orderByNode = new Dictionary<string, int>();
            var followed = new HashSet<(string, string)>();
            var inputValuesByNode = new Dictionary<string, List<(string, string)>>();
            var computedValueByNode = new Dictionary<string, string>();

            if (segment != null)
            {
                int order = 0;
                foreach (RuleTraceEntry entry in segment.Entries)
                {
                    switch (entry.Kind)
                    {
                        case RuleTraceEntryKind.NodeExecuted:
                            if (!orderByNode.ContainsKey(entry.NodeId))
                            {
                                order++;
                                orderByNode[entry.NodeId] = order;
                            }

                            if (entry.PortOrFieldName != null)
                            {
                                followed.Add((entry.NodeId, entry.PortOrFieldName));
                            }

                            break;

                        case RuleTraceEntryKind.InputRead:
                            if (!inputValuesByNode.TryGetValue(entry.NodeId, out List<(string, string)> list))
                            {
                                list = new List<(string, string)>();
                                inputValuesByNode[entry.NodeId] = list;
                            }

                            list.Add((entry.PortOrFieldName, entry.Value.HasValue ? entry.Value.Value.ToString() : string.Empty));
                            break;

                        case RuleTraceEntryKind.ValueComputed:
                            computedValueByNode[entry.NodeId] = entry.Value.HasValue ? entry.Value.Value.ToString() : string.Empty;
                            break;
                    }
                }
            }

            return new RuleHighlightData(orderByNode, followed, inputValuesByNode, computedValueByNode, segment?.EndNodeId, segment?.EndErrorMessage);
        }
    }
}
