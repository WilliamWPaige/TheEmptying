using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R11: the shipped starting site (starting_sites.json) loads and validates - for now exactly one, at the map center.</summary>
    public class StartingSitesTests
    {
        private static ContentDatabase LoadRealContent()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);
            return simulation.Content;
        }

        [Test]
        public void StartingSites_LoadsExactlyOneSite_AtTheMapCenter()
        {
            ContentDatabase content = LoadRealContent();
            List<StartingSiteDefinition> sites = BuiltInContentTypes.StartingSites(content);

            Assert.AreEqual(1, sites.Count);
            Assert.AreEqual("site.center", sites[0].Id);
            Assert.AreEqual(0.5, sites[0].FractionX);
            Assert.AreEqual(0.5, sites[0].FractionZ);
        }
    }
}
