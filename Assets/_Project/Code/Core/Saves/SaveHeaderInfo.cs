using System.Collections.Generic;

namespace TheEmptying.Core.Saves
{
    /// <summary>The parts of the header a caller supplies; the rest is derived from the Simulation.</summary>
    public sealed class SaveHeaderInfo
    {
        public string KingdomName { get; set; }
        public long Population { get; set; }
        public List<SaveSourceInfo> Sources { get; set; } = new List<SaveSourceInfo>();
    }
}
