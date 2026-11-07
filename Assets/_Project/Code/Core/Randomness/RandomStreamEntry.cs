namespace TheEmptying.Core.Randomness
{
    public sealed class RandomStreamEntry
    {
        public string Name { get; set; }
        public ulong State { get; set; }
        public ulong Inc { get; set; }
    }
}
