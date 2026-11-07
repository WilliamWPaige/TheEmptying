namespace TheEmptying.Core.Randomness
{
    public readonly struct SimRandomState
    {
        public ulong State { get; }
        public ulong Inc { get; }

        public SimRandomState(ulong state, ulong inc)
        {
            State = state;
            Inc = inc;
        }
    }
}
