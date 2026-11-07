using System.Collections.Generic;

namespace TheEmptying.Core.Entities
{
    /// <summary>
    /// Plain data snapshot of an EntityRegistry. The real save system (prompt 1.7)
    /// decides how this gets written to disk. Registered tables are not part of
    /// this state; they re-register themselves when a module sets back up.
    /// </summary>
    public sealed class EntityRegistryState
    {
        public List<int> Generations { get; set; } = new List<int>();
        public List<bool> Alive { get; set; } = new List<bool>();
        public List<EntityKind> Kinds { get; set; } = new List<EntityKind>();

        /// <summary>Freed indices, oldest-freed first (dequeue order).</summary>
        public List<int> FreeIndices { get; set; } = new List<int>();
    }
}
