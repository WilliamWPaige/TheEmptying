using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
using TheEmptying.Presentation;

namespace TheEmptying.Editor.Rules
{
    /// <summary>
    /// Builds the full BlockRegistry exactly the way the game does: core
    /// blocks, then the declaration phase of every module in the shared
    /// ModuleCatalog list (honoring each module's on/off switch and
    /// dependency order from the data manifests). No content is loaded, no
    /// module is initialized, and nothing runs - this is only enough of the
    /// startup pipeline to know which block types exist.
    /// </summary>
    public static class EditorBlockCatalog
    {
        public static BlockRegistry Build(IEnumerable<string> sourceFolders, IEnumerable<IModule> modules = null, ErrorMode errorMode = ErrorMode.Development)
        {
            List<string> sources = sourceFolders?.ToList() ?? new List<string>();
            List<IModule> moduleList = (modules ?? ModuleCatalog.CreateModules()).ToList();

            Dictionary<string, ModuleManifest> manifests = ModuleManifestBootstrap.LoadManifests(sources);

            var simulation = new Simulation(1, errorMode);
            simulation.Modules.ValidateAndOrder(moduleList, manifests);

            BuiltInContentTypes.RegisterAll(simulation.ContentTypes);
            CoreBlocks.RegisterAll(simulation.Blocks);
            simulation.Modules.RunDeclarationPhase(simulation.ContentTypes, simulation.Settings, simulation.Blocks);

            return simulation.Blocks;
        }
    }
}
