using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Rules.Tracing
{
    /// <summary>
    /// One entry in a trace segment's ordered timeline. Values are captured at
    /// the moment the runner computed or read them - never recomputed, never
    /// re-read - so a trace can never itself change what a rule does.
    /// </summary>
    public sealed class RuleTraceEntry
    {
        public RuleTraceEntryKind Kind { get; }
        public string NodeId { get; }

        /// <summary>NodeExecuted: the output port followed, or null. InputRead: the input port or field name. Otherwise null.</summary>
        public string PortOrFieldName { get; }

        /// <summary>ValueComputed / InputRead / LocalSet's value. Null otherwise.</summary>
        public RuleValue? Value { get; }

        /// <summary>LineLogged's text, or EventPosted's event definition Id. Null otherwise.</summary>
        public string Message { get; }

        /// <summary>EventPosted's parameters. Empty otherwise.</summary>
        public IReadOnlyList<(string Name, RuleValue Value)> EventParameters { get; }

        private RuleTraceEntry(RuleTraceEntryKind kind, string nodeId, string portOrFieldName, RuleValue? value, string message, IReadOnlyList<(string Name, RuleValue Value)> eventParameters)
        {
            Kind = kind;
            NodeId = nodeId;
            PortOrFieldName = portOrFieldName;
            Value = value;
            Message = message;
            EventParameters = eventParameters ?? Array.Empty<(string, RuleValue)>();
        }

        public static RuleTraceEntry NodeExecuted(string nodeId, string followedPort) =>
            new RuleTraceEntry(RuleTraceEntryKind.NodeExecuted, nodeId, followedPort, null, null, null);

        public static RuleTraceEntry ValueComputed(string nodeId, RuleValue value) =>
            new RuleTraceEntry(RuleTraceEntryKind.ValueComputed, nodeId, null, value, null, null);

        public static RuleTraceEntry InputRead(string nodeId, string portOrFieldName, RuleValue value) =>
            new RuleTraceEntry(RuleTraceEntryKind.InputRead, nodeId, portOrFieldName, value, null, null);

        public static RuleTraceEntry LocalSet(string nodeId, string localName, RuleValue value) =>
            new RuleTraceEntry(RuleTraceEntryKind.LocalSet, nodeId, localName, value, null, null);

        public static RuleTraceEntry EventPosted(string nodeId, string eventDefinitionId, IReadOnlyList<(string Name, RuleValue Value)> parameters) =>
            new RuleTraceEntry(RuleTraceEntryKind.EventPosted, nodeId, null, null, eventDefinitionId, parameters);

        public static RuleTraceEntry LineLogged(string nodeId, string message) =>
            new RuleTraceEntry(RuleTraceEntryKind.LineLogged, nodeId, null, null, message, null);

        public override string ToString()
        {
            return Kind switch
            {
                RuleTraceEntryKind.NodeExecuted => PortOrFieldName == null ? $"{NodeId} ran" : $"{NodeId} ran -> {PortOrFieldName}",
                RuleTraceEntryKind.ValueComputed => $"{NodeId} computed {Value}",
                RuleTraceEntryKind.InputRead => $"{NodeId}.{PortOrFieldName} read {Value}",
                RuleTraceEntryKind.LocalSet => $"{NodeId} set local '{PortOrFieldName}' = {Value}",
                RuleTraceEntryKind.EventPosted => $"{NodeId} posted '{Message}'",
                RuleTraceEntryKind.LineLogged => $"{NodeId} logged: {Message}",
                _ => Kind.ToString()
            };
        }
    }
}
