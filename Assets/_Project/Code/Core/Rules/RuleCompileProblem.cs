namespace TheEmptying.Core.Rules
{
    /// <summary>One compiler error or warning: which rule, which node (if any), and why.</summary>
    public sealed class RuleCompileProblem
    {
        public string RuleId { get; }
        public string NodeId { get; }
        public string Message { get; }

        public RuleCompileProblem(string ruleId, string nodeId, string message)
        {
            RuleId = ruleId;
            NodeId = nodeId;
            Message = message;
        }

        public override string ToString()
        {
            return NodeId == null ? $"[{RuleId}] {Message}" : $"[{RuleId}/{NodeId}] {Message}";
        }
    }
}
