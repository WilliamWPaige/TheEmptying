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
    /// <summary>R12: the shipped zone types (zone_types.json) load and validate - for now exactly Stockpile and Living Area, each with a non-empty display name and a valid color.</summary>
    public class ZoneTypesTests
    {
        private static ContentDatabase LoadRealContent()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);
            return simulation.Content;
        }

        [Test]
        public void ZoneTypes_LoadsExactlyStockpileAndLivingArea()
        {
            ContentDatabase content = LoadRealContent();
            List<ZoneTypeDefinition> zoneTypes = BuiltInContentTypes.ZoneTypes(content);

            CollectionAssert.AreEquivalent(new[] { "zone.stockpile", "zone.living_area" }, zoneTypes.ConvertAll(z => z.Id));
        }

        [TestCase("zone.stockpile", "#3DA5D9")]
        [TestCase("zone.living_area", "#8BC34A")]
        public void ZoneTypes_HaveTheExpectedColorAndANonEmptyDisplayName(string id, string expectedColorHex)
        {
            ContentDatabase content = LoadRealContent();
            List<ZoneTypeDefinition> zoneTypes = BuiltInContentTypes.ZoneTypes(content);
            ZoneTypeDefinition zoneType = zoneTypes.Find(z => z.Id == id);

            Assert.IsNotNull(zoneType);
            Assert.IsNotEmpty(zoneType.DisplayName);
            Assert.AreEqual(expectedColorHex, zoneType.ColorHex);
        }
    }
}
