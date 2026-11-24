using TheEmptying.Modules.Discovery;

namespace TheEmptying.Presentation.DevTools
{
    /// <summary>R14 test tool: the cheat menu's "Discovery" section - instantly discover everything, or forget every discovery.</summary>
    public sealed class DiscoveryCheat
    {
        private readonly DiscoveryModule _discoveryModule;

        public DiscoveryCheat(DiscoveryModule discoveryModule)
        {
            _discoveryModule = discoveryModule;
        }

        public void BuildSections(CheatMenuRegistry registry)
        {
            CheatSection discovery = registry.AddSection("Discovery");
            discovery.AddButton("Discover everything", _discoveryModule.DiscoverEverything);
            discovery.AddButton("Reset discoveries", _discoveryModule.ResetDiscoveries);
        }
    }
}
