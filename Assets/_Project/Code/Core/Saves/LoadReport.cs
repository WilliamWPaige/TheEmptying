using System.Collections.Generic;

namespace TheEmptying.Core.Saves
{
    public sealed class LoadReport
    {
        /// <summary>A mod missing, added, or at a different version than when the save was made.</summary>
        public List<string> SourceDifferences { get; } = new List<string>();

        /// <summary>An enabled module with no section in the save; it started fresh.</summary>
        public List<string> ModulesStartingFresh { get; } = new List<string>();

        /// <summary>Release mode only: a module switched off because its saved section was newer than it understands.</summary>
        public List<string> NewerSectionVersions { get; } = new List<string>();
    }
}
