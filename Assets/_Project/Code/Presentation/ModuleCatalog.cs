using TheEmptying.Core.Modules;

namespace TheEmptying.Presentation
{
    /// <summary>
    /// Every module the game knows how to run, regardless of whether its
    /// manifest enables it. The single shared list the game scene and editor
    /// tools (the block catalog) both build from, so neither can drift out of
    /// sync with the other. A new instance each call, since modules hold
    /// per-game state.
    ///
    /// Empty since R0 removed the Milestone 3 town simulation for the
    /// rebuild; modules are added back here as each one is rebuilt.
    /// </summary>
    public static class ModuleCatalog
    {
        public static IModule[] CreateModules()
        {
            return System.Array.Empty<IModule>();
        }
    }
}
