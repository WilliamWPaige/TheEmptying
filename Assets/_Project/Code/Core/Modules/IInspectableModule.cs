using System.Collections.Generic;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Modules
{
    /// <summary>Optional. A module implements this to describe its data for a given entity, for the inspector and the "inspect" console command.</summary>
    public interface IInspectableModule
    {
        IEnumerable<string> Inspect(EntityId entity);
    }
}
