using System;
using System.Collections.Generic;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;

namespace TheEmptying.Tests.EditMode.Saves
{
    /// <summary>A module whose SectionVersion is set by the test, to exercise "newer section version" handling.</summary>
    internal sealed class VersionedModule : IModule, ISaveableModule
    {
        public string Id => "test.versioned";
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();
        public int SectionVersion { get; }

        public VersionedModule(int sectionVersion)
        {
            SectionVersion = sectionVersion;
        }

        public void Initialize(ModuleContext context)
        {
        }

        public void Shutdown()
        {
        }

        public void Write(SaveWriter writer) => writer.WriteLong(42);

        public void Read(SaveReader reader, int savedVersion) => reader.ReadLong();
    }
}
