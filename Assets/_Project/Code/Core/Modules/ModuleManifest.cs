using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Modules
{
    /// <summary>Supplied from code for now; prompt 1.6 loads these from data.</summary>
    public sealed class ModuleManifest
    {
        public string Id { get; }
        public int Version { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public bool Enabled { get; }

        public ModuleManifest(string id, int version, IReadOnlyList<string> dependencies, bool enabled)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("id must not be empty.", nameof(id));
            }

            Id = id;
            Version = version;
            Dependencies = dependencies ?? Array.Empty<string>();
            Enabled = enabled;
        }
    }
}
