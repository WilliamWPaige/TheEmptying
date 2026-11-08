using TheEmptying.Core.Rules;
using UnityEngine;

namespace TheEmptying.Editor.Rules.Canvas
{
    /// <summary>
    /// One port on a drawn node: its definition (name, direction, kind, data
    /// type), whether it was inferred rather than read from a real block type
    /// (a missing-block node), and where it sits relative to its node's
    /// top-left corner, in canvas units.
    /// </summary>
    public sealed class PortViewModel
    {
        public string Name { get; }
        public PortDirection Direction { get; }
        public PortKind Kind { get; }
        public RuleDataType? DataType { get; }

        /// <summary>True for a port that couldn't be read from a real block type and was instead guessed from the rule's connections (a missing-block node).</summary>
        public bool IsInferred { get; }

        /// <summary>Where this port's anchor sits relative to its node's top-left corner, in canvas units. Set once by NodeViewModel right after construction.</summary>
        public Vector2 LocalOffset { get; internal set; }

        public PortViewModel(string name, PortDirection direction, PortKind kind, RuleDataType? dataType, bool isInferred)
        {
            Name = name;
            Direction = direction;
            Kind = kind;
            DataType = dataType;
            IsInferred = isInferred;
        }
    }
}
