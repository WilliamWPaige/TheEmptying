using TheEmptying.Core.Modules;
using TheEmptying.Modules.Buildings;
using TheEmptying.Modules.Discovery;
using TheEmptying.Modules.Map;
using TheEmptying.Modules.People;
using TheEmptying.Modules.Zones;

namespace TheEmptying.Presentation
{
    /// <summary>
    /// Every module the game knows how to run, regardless of whether its
    /// manifest enables it. The single shared list the game scene and editor
    /// tools (the block catalog) both build from, so neither can drift out of
    /// sync with the other. A new instance each call, since modules hold
    /// per-game state.
    /// </summary>
    public static class ModuleCatalog
    {
        public static IModule[] CreateModules()
        {
            var map = new MapModule();
            var buildings = new BuildingsModule(map);
            var zones = new ZonesModule(map, buildings);
            return new IModule[] { map, buildings, zones, new PeopleModule(map, buildings, zones), new DiscoveryModule() };
        }
    }
}
