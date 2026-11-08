using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;

namespace TheEmptying.Core.Startup
{
    /// <summary>
    /// Runs the full startup pipeline in one place: bootstrap manifests from
    /// data, create and order the Simulation, declare, load and validate
    /// content, register content-defined event definitions and settings,
    /// compile rules, then initialize modules. New games and loads both go
    /// through this.
    /// </summary>
    public static class SimulationBuilder
    {
        public static (Simulation Simulation, StartupReport Report) Build(
            ulong worldSeed,
            IEnumerable<string> sourceFolders,
            IEnumerable<IModule> modules,
            ErrorMode errorMode = ErrorMode.Development,
            bool isLoading = false,
            ILogSink logSink = null)
        {
            List<string> sources = sourceFolders?.ToList() ?? new List<string>();
            List<IModule> moduleList = modules?.ToList() ?? new List<IModule>();

            // 1. Bootstrap manifests from the sources.
            Dictionary<string, ModuleManifest> manifests = ModuleManifestBootstrap.LoadManifests(sources);

            // 2. Create the Simulation; validate, order and apply disabling using the data manifests.
            var simulation = new Simulation(worldSeed, errorMode);
            simulation.Modules.ValidateAndOrder(moduleList, manifests);

            // 3. Run the declaration phase (built-in content types must exist first; the core registers its own block types too).
            BuiltInContentTypes.RegisterAll(simulation.ContentTypes);
            CoreBlocks.RegisterAll(simulation.Blocks);
            simulation.Modules.RunDeclarationPhase(simulation.ContentTypes, simulation.Settings, simulation.Blocks);

            // 4. Load and validate all content.
            var loader = new ContentLoader(simulation.ContentTypes, errorMode);
            (ContentDatabase database, ContentLoadReport contentReport) = loader.Load(sources);
            simulation.Content = database;

            // 5. Register event definitions and settings from the loaded content.
            BuiltInContentTypes.RegisterEventDefinitionsFrom(database, simulation.EventDefinitions);
            BuiltInContentTypes.DeclareSettingsFrom(database, simulation.Settings);

            // 6. Compile rules (after content loads, before modules initialize).
            (RuleRegistry ruleRegistry, RuleCompileReport ruleReport) = RuleCompiler.Compile(database, simulation.Blocks, simulation.Settings, simulation.EventDefinitions, errorMode);
            simulation.Rules = ruleRegistry;

            // 7. Initialize modules.
            simulation.Modules.InitializeModules(isLoading);

            // 8. Activate the rule runner (after modules initialize).
            var ruleRunner = new RuleRunner(ruleRegistry, simulation.Events, simulation.Scheduler, simulation.Random, simulation.Settings, simulation.EventDefinitions, logSink, errorMode, () => simulation.Clock.CurrentTick);
            ruleRunner.Activate();
            simulation.RuleRunner = ruleRunner;

            var report = new StartupReport(simulation.Modules.DisabledReasons, contentReport, ruleReport);
            return (simulation, report);
        }
    }
}
