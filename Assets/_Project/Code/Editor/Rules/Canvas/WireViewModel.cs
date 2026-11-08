using TheEmptying.Core.Rules;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas
{
    /// <summary>One wire drawn between two ports, with its canvas-space endpoints resolved from its nodes' current positions.</summary>
    public sealed class WireViewModel
    {
        public RuleConnection Connection { get; }
        public NodeViewModel FromNode { get; }
        public PortViewModel FromPort { get; }
        public NodeViewModel ToNode { get; }
        public PortViewModel ToPort { get; }

        /// <summary>Execution wires draw thicker and neutral-colored; data wires draw in their type's color (null means a wildcard or an inferred, unknown-typed port).</summary>
        public PortKind Kind => FromPort.Kind;
        public RuleDataType? DataType => FromPort.DataType;

        public Vector2 FromAnchor => FromNode.Position + FromPort.LocalOffset;
        public Vector2 ToAnchor => ToNode.Position + ToPort.LocalOffset;

        public WireViewModel(RuleConnection connection, NodeViewModel fromNode, PortViewModel fromPort, NodeViewModel toNode, PortViewModel toPort)
        {
            Connection = connection;
            FromNode = fromNode;
            FromPort = fromPort;
            ToNode = toNode;
            ToPort = toPort;
        }
    }
}
