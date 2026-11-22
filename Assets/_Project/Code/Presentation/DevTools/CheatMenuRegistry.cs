using System.Collections.Generic;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>
    /// The Cheats tab's one registration point. Each system adds its own
    /// section with a single call: registry.AddSection("Title").AddButton(...).
    /// Rebuilt (Clear, then add sections again) whenever the simulation it
    /// reads from is rebuilt (a new game or a load).
    /// </summary>
    public sealed class CheatMenuRegistry
    {
        private readonly List<CheatSection> _sections = new List<CheatSection>();

        public IReadOnlyList<CheatSection> Sections => _sections;

        public CheatSection AddSection(string title)
        {
            var section = new CheatSection(title);
            _sections.Add(section);
            return section;
        }

        public void Clear()
        {
            _sections.Clear();
        }
    }
}
