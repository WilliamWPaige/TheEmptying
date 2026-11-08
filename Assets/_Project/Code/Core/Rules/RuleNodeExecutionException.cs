using System;

namespace TheEmptying.Core.Rules
{
    /// <summary>Wraps a block's thrown exception with the node that threw it, so the outermost catch can attribute the failure correctly regardless of call depth.</summary>
    internal sealed class RuleNodeExecutionException : Exception
    {
        public string NodeId { get; }

        public RuleNodeExecutionException(string nodeId, Exception innerException)
            : base(innerException.Message, innerException)
        {
            NodeId = nodeId;
        }
    }
}
