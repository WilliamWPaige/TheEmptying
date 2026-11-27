using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation;
using TheEmptying.Presentation.Zones;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R18b: the shipped zones settings (zones_settings.json) load with the right defaults, ranges and categories.</summary>
    public class ZonesSettingsTests
    {
        [TestCase(ZonesModule.SpotSizeMetersSettingId, SettingValueType.Number, 2.0, 1.0, 10.0, "Zones")]
        [TestCase(ZonesModule.CapacityUnitsPerQuarterSettingId, SettingValueType.Integer, 25.0, 1.0, 1000.0, "Zones")]
        [TestCase(StockpileStackPresenter.PileBoxCoverageFractionSettingId, SettingValueType.Number, 0.85, 0.1, 1.0, "Zones")]
        [TestCase(ZonesModule.NaturalPatchMinSpotsPerSideSettingId, SettingValueType.Integer, 2.0, 1.0, 10.0, "Zones")]
        [TestCase(ZonesModule.NaturalPatchMaxSpotsPerSideSettingId, SettingValueType.Integer, 4.0, 1.0, 10.0, "Zones")]
        [TestCase(ZonesModule.NaturalPatchMinAngleDegreesSettingId, SettingValueType.Number, 0.0, 0.0, 360.0, "Zones")]
        [TestCase(ZonesModule.NaturalPatchMaxAngleDegreesSettingId, SettingValueType.Number, 90.0, 0.0, 360.0, "Zones")]
        [TestCase(ZonesModule.NaturalMaxPlacementAttemptsSettingId, SettingValueType.Integer, 200.0, 1.0, 5000.0, "Zones")]
        public void Setting_LoadsFromRealContent_WithExpectedDefaultRangeAndCategory(
            string settingId, SettingValueType expectedType, double expectedDefault, double expectedMinimum, double expectedMaximum, string expectedCategory)
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);
            SettingsRegistry settings = simulation.Settings;

            Assert.IsTrue(settings.TryGetDefinition(settingId, out SettingDefinition definition), $"Expected '{settingId}' to be declared.");
            Assert.AreEqual(expectedType, definition.ValueType);
            Assert.AreEqual(expectedDefault, definition.Default);
            Assert.AreEqual(expectedMinimum, definition.Minimum);
            Assert.AreEqual(expectedMaximum, definition.Maximum);
            Assert.AreEqual(expectedCategory, definition.Category);
            Assert.AreEqual(expectedDefault, settings.Get(settingId), "The live value should start at the default.");
        }
    }
}
