using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Core.Rules.CoreBlocks;
using TheEmptying.Presentation;

namespace TheEmptying.Editor.Rules
{
    /// <summary>
    /// Everything the rule editor needs to know about the game, built the same
    /// way the game itself builds it: the block registry (core blocks, then
    /// every module in the shared ModuleCatalog's declaration phase, honoring
    /// on/off and dependency order), settings (content-declared plus
    /// module-declared), event definitions (content-registered plus
    /// code-registered, including the core's own calendar and rule-runtime
    /// events), and every loaded content Id grouped by content type. No
    /// module is initialized and no simulation runs. Rebuild (call Build
    /// again) when content files change or the user asks for a refresh.
    /// </summary>
    public sealed class EditorContext
    {
        public BlockRegistry Blocks { get; }
        public SettingsRegistry Settings { get; }
        public EventDefinitionRegistry Events { get; }
        public ContentDatabase Content { get; }
        public ContentLoadReport ContentReport { get; }

        /// <summary>Every loaded content Id, grouped by content type name, in ascending Id order - for content-Id pickers.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> ContentIdsByType { get; }

        public IReadOnlyList<string> SourceFolders { get; }

        private EditorContext(
            BlockRegistry blocks,
            SettingsRegistry settings,
            EventDefinitionRegistry events,
            ContentDatabase content,
            ContentLoadReport contentReport,
            IReadOnlyDictionary<string, IReadOnlyList<string>> contentIdsByType,
            IReadOnlyList<string> sourceFolders)
        {
            Blocks = blocks;
            Settings = settings;
            Events = events;
            Content = content;
            ContentReport = contentReport;
            ContentIdsByType = contentIdsByType;
            SourceFolders = sourceFolders;
        }

        public static EditorContext Build(IEnumerable<string> sourceFolders, IEnumerable<IModule> modules = null, ErrorMode errorMode = ErrorMode.Development)
        {
            List<string> sources = sourceFolders?.ToList() ?? new List<string>();
            List<IModule> moduleList = (modules ?? ModuleCatalog.CreateModules()).ToList();

            Dictionary<string, ModuleManifest> manifests = ModuleManifestBootstrap.LoadManifests(sources);

            var simulation = new Simulation(1, errorMode);
            simulation.Modules.ValidateAndOrder(moduleList, manifests);

            BuiltInContentTypes.RegisterAll(simulation.ContentTypes);
            CoreBlocks.RegisterAll(simulation.Blocks);
            simulation.Modules.RunDeclarationPhase(simulation.ContentTypes, simulation.Settings, simulation.Blocks);

            var loader = new ContentLoader(simulation.ContentTypes, errorMode);
            (ContentDatabase database, ContentLoadReport contentReport) = loader.Load(sources);

            BuiltInContentTypes.RegisterEventDefinitionsFrom(database, simulation.EventDefinitions);
            BuiltInContentTypes.DeclareSettingsFrom(database, simulation.Settings);

            Dictionary<string, IReadOnlyList<string>> contentIdsByType = BuildContentIdIndex(simulation.ContentTypes, database);

            return new EditorContext(simulation.Blocks, simulation.Settings, simulation.EventDefinitions, database, contentReport, contentIdsByType, sources);
        }

        private static Dictionary<string, IReadOnlyList<string>> BuildContentIdIndex(ContentTypeRegistry types, ContentDatabase database)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>();
            foreach (string typeName in types.TypeNames)
            {
                result[typeName] = database.ListByType(typeName).Select(e => e.Definition.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            }

            return result;
        }
    }
}
