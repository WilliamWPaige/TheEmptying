using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Saves
{
    /// <summary>Readable without loading or decompressing the rest of the file.</summary>
    public sealed class SaveHeader
    {
        public int FormatVersion { get; set; }
        public string GameVersion { get; set; }
        public string KingdomName { get; set; }
        public string InGameDate { get; set; }
        public long CurrentTick { get; set; }
        public long Population { get; set; }
        public ulong WorldSeed { get; set; }
        public List<SaveSourceInfo> Sources { get; set; } = new List<SaveSourceInfo>();
        public DateTime SavedAtUtc { get; set; }
    }
}
