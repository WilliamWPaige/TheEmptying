using System;
using TheEmptying.Core.Events;

namespace TheEmptying.Core.Rules
{
    /// <summary>Thrown in Development ErrorMode when a rule throws during execution.</summary>
    public sealed class RuleException : Exception, IAttributedFailure
    {
        public string RuleId { get; }
        public string NodeId { get; }

        public RuleException(string message, string ruleId, string nodeId, Exception innerException)
            : base(message, innerException)
        {
            RuleId = ruleId;
            NodeId = nodeId;
        }
    }
}
