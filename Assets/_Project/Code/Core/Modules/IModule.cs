using System.Collections.Generic;

namespace TheEmptying.Core.Modules
{
    public interface IModule
    {
        string Id { get; }
        int Version { get; }
        IReadOnlyList<string> Dependencies { get; }

        void Initialize(ModuleContext context);
        void Shutdown();
    }
}
