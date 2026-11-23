using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R9: every shipped setting has a non-empty label, so the Cheats tab never shows a raw Id where a label belongs.</summary>
    public class SettingLabelsTests
    {
        [Test]
        public void EveryShippedSetting_HasANonEmptyLabel()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);

            SettingDefinition[] withoutLabel = simulation.Settings.ExportAll()
                .Where(definition => string.IsNullOrEmpty(definition.Label))
                .ToArray();

            Assert.IsEmpty(withoutLabel, "Settings missing a label: " + string.Join(", ", withoutLabel.Select(d => d.Id)));
        }
    }
}
