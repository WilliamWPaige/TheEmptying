namespace TheEmptying.Core.Rules.Tracing
{
    public enum RuleTraceEntryKind
    {
        /// <summary>A node ran. PortOrFieldName is the execution output it followed (null for End, for a Sequence's own hop, for Wait's own hop before its resume port, or for Ask).</summary>
        NodeExecuted,

        /// <summary>A pure-value node computed its output (once - the value cache means this fires at most once per node per step).</summary>
        ValueComputed,

        /// <summary>A value was read for a node's input port or field (wire or field, whichever won).</summary>
        InputRead,

        /// <summary>A local variable was set.</summary>
        LocalSet,

        /// <summary>An event was posted.</summary>
        EventPosted,

        /// <summary>A log or herald line was written (both go through the same Log call; a herald line's event post gets its own separate EventPosted entry).</summary>
        LineLogged
    }
}
