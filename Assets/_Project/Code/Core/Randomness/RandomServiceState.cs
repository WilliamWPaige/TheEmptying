using System.Collections.Generic;

namespace TheEmptying.Core.Randomness
{
    /// <summary>
    /// Plain data snapshot of every stream a RandomService has created so far.
    /// The real save system (prompt 1.7) will decide how this gets written to disk.
    /// </summary>
    public sealed class RandomServiceState
    {
        public List<RandomStreamEntry> Streams { get; set; } = new List<RandomStreamEntry>();
    }
}
