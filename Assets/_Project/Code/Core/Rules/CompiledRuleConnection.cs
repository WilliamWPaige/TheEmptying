namespace TheEmptying.Core.Rules
{
    /// <summary>A wire resolved to port indices within each endpoint's BlockType.Ports list.</summary>
    public sealed class CompiledRuleConnection
    {
        public string FromNode { get; }
        public int FromPortIndex { get; }
        public string ToNode { get; }
        public int ToPortIndex { get; }

        public CompiledRuleConnection(string fromNode, int fromPortIndex, string toNode, int toPortIndex)
        {
            FromNode = fromNode;
            FromPortIndex = fromPortIndex;
            ToNode = toNode;
            ToPortIndex = toPortIndex;
        }
    }
}
