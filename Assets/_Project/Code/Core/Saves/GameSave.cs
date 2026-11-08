using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;

namespace TheEmptying.Core.Saves
{
    /// <summary>
    /// Save(Simulation, header info, Stream) and Load(Stream, sources, modules).
    /// File paths are decided by the presentation layer.
    /// </summary>
    public static class GameSave
    {
        public static void Save(Simulation simulation, SaveHeaderInfo headerInfo, Stream stream)
        {
            var header = new SaveHeader
            {
                FormatVersion = SaveFile.FormatVersion,
                GameVersion = ProjectInfo.Version,
                KingdomName = headerInfo.KingdomName,
                InGameDate = simulation.Clock.CurrentDate.ToString(),
                CurrentTick = simulation.Clock.CurrentTick,
                Population = headerInfo.Population,
                WorldSeed = simulation.Random.WorldSeed,
                Sources = headerInfo.Sources,
                SavedAtUtc = headerInfo.SavedAtUtc
            };

            List<SaveSectionData> sections = CoreSections.WriteAll(simulation, simulation.Settings, simulation.EventSerializers);

            var writtenModuleIds = new HashSet<string>();
            foreach ((string moduleId, ISaveableModule module) in simulation.Modules.GetActiveSaveableModules())
            {
                sections.Add(new SaveSectionData(moduleId, module.SectionVersion, SaveSectionWriter.WriteModule(module)));
                writtenModuleIds.Add(moduleId);
            }

            // Sections for modules currently missing or switched off are carried forward untouched.
            foreach (KeyValuePair<string, SaveSectionData> orphan in simulation.OrphanedModuleSections)
            {
                if (!writtenModuleIds.Contains(orphan.Key))
                {
                    sections.Add(orphan.Value);
                }
            }

            SaveFile.Write(stream, header, sections);
        }

        /// <summary>
        /// Loads through SimulationBuilder, so content reloads the same way a
        /// new game would load it. sourceFolders supplies both the content and
        /// the "what's currently available" side of the source-difference report.
        /// </summary>
        public static (Simulation Simulation, LoadReport Report) Load(
            Stream stream,
            IEnumerable<string> sourceFolders,
            IEnumerable<IModule> modules,
            ErrorMode errorMode = ErrorMode.Development,
            ILogSink logSink = null)
        {
            (SaveHeader header, Dictionary<string, SaveSectionData> sections) = SaveFile.ReadAll(stream);

            var report = new LoadReport();
            List<SaveSourceInfo> currentSources = SourceManifestReader.ReadAll(sourceFolders);
            CompareSources(header, currentSources, report);

            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(header.WorldSeed, sourceFolders, modules, errorMode, isLoading: true, logSink: logSink);

            CoreSections.ReadAll(sections, simulation, simulation.Settings, simulation.EventSerializers);

            var consumedSectionIds = new HashSet<string>(CoreSections.AllIds);

            foreach ((string moduleId, ISaveableModule module) in simulation.Modules.GetActiveSaveableModules())
            {
                consumedSectionIds.Add(moduleId);

                if (!sections.TryGetValue(moduleId, out SaveSectionData section))
                {
                    report.ModulesStartingFresh.Add(moduleId);
                    continue;
                }

                if (section.Version > module.SectionVersion)
                {
                    report.NewerSectionVersions.Add($"Module '{moduleId}': saved section version {section.Version}, understood {module.SectionVersion}.");
                    simulation.Modules.HandleSectionVersionTooNew(moduleId, section.Version, module.SectionVersion);
                    continue;
                }

                using var stream2 = new MemoryStream(section.Data);
                var reader = new SaveReader(stream2);
                simulation.Modules.ReadModuleSection(moduleId, module, reader, section.Version);
            }

            foreach (KeyValuePair<string, SaveSectionData> entry in sections)
            {
                if (!consumedSectionIds.Contains(entry.Key))
                {
                    simulation.OrphanedModuleSections[entry.Key] = entry.Value;
                }
            }

            return (simulation, report);
        }

        private static void CompareSources(SaveHeader header, List<SaveSourceInfo> currentSources, LoadReport report)
        {
            Dictionary<string, SaveSourceInfo> currentById = currentSources
                .ToDictionary(s => s.Id, s => s, System.StringComparer.Ordinal);
            Dictionary<string, SaveSourceInfo> savedById = header.Sources.ToDictionary(s => s.Id, s => s, System.StringComparer.Ordinal);

            foreach (SaveSourceInfo saved in header.Sources)
            {
                if (!currentById.TryGetValue(saved.Id, out SaveSourceInfo current))
                {
                    report.SourceDifferences.Add($"Source '{saved.Id}' (version {saved.Version}) was used in this save but is not currently available.");
                }
                else if (current.Version != saved.Version)
                {
                    report.SourceDifferences.Add($"Source '{saved.Id}' version changed: save used {saved.Version}, currently {current.Version}.");
                }
            }

            foreach (SaveSourceInfo current in currentById.Values)
            {
                if (!savedById.ContainsKey(current.Id))
                {
                    report.SourceDifferences.Add($"Source '{current.Id}' (version {current.Version}) is available now but was not used in this save.");
                }
            }
        }
    }
}
