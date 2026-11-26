using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Saves
{
    /// <summary>The parts of the header a caller supplies; the rest is derived from the Simulation.</summary>
    public sealed class SaveHeaderInfo
    {
        /// <summary>R19: the running game's version (Application.version) - supplied by the presentation layer, since the simulation never references UnityEngine. Project Settings &gt; Player &gt; Version is the only place the version itself is stored.</summary>
        public string GameVersion { get; set; }

        public string KingdomName { get; set; }
        public long Population { get; set; }
        public List<SaveSourceInfo> Sources { get; set; } = new List<SaveSourceInfo>();

        /// <summary>Real-world wall-clock time the save was made, supplied by the presentation layer.</summary>
        public DateTime SavedAtUtc { get; set; }
    }
}
