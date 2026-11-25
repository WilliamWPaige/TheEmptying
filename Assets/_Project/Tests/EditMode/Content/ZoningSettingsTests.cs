using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Presentation;
using TheEmptying.Presentation.World;
using TheEmptying.Presentation.Zones;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R18: the shipped zoning settings (zoning_settings.json) load with the right defaults, ranges and categories.</summary>
    public class ZoningSettingsTests
    {
        private static SettingsRegistry BuildFromRealContent()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);
            return simulation.Settings;
        }

        [TestCase(StrategyCameraController.ZoningTransitionSecondsSettingId, SettingValueType.Number, 0.5, 0.1, 5.0, "Zoning")]
        [TestCase(StrategyCameraController.ZoningKeepRotationFacingSettingId, SettingValueType.Boolean, 0.0, 0.0, 1.0, "Zoning")]
        [TestCase(ZoningMenuPresenter.BrushMinSizeCellsSettingId, SettingValueType.Integer, 1.0, 1.0, 10.0, "Zoning")]
        [TestCase(ZoningMenuPresenter.BrushMaxSizeCellsSettingId, SettingValueType.Integer, 10.0, 1.0, 20.0, "Zoning")]
        public void Setting_LoadsFromRealContent_WithExpectedDefaultRangeAndCategory(
            string settingId, SettingValueType expectedType, double expectedDefault, double expectedMinimum, double expectedMaximum, string expectedCategory)
        {
            SettingsRegistry settings = BuildFromRealContent();

            Assert.IsTrue(settings.TryGetDefinition(settingId, out SettingDefinition definition), $"Expected '{settingId}' to be declared.");
            Assert.AreEqual(expectedType, definition.ValueType);
            Assert.AreEqual(expectedDefault, definition.Default);
            Assert.AreEqual(expectedMinimum, definition.Minimum);
            Assert.AreEqual(expectedMaximum, definition.Maximum);
            Assert.AreEqual(expectedCategory, definition.Category);
            Assert.AreEqual(expectedDefault, settings.Get(settingId), "The live value should start at the default.");
        }

        [Test]
        public void KeepRotationFacing_DefaultsOff_SoZoningStartsNorthUp()
        {
            SettingsRegistry settings = BuildFromRealContent();
            Assert.AreEqual(0.0, settings.Get(StrategyCameraController.ZoningKeepRotationFacingSettingId));
        }

        [Test]
        public void BrushMinSize_IsLessThanOrEqualToBrushMaxSize()
        {
            SettingsRegistry settings = BuildFromRealContent();
            Assert.LessOrEqual(
                settings.Get(ZoningMenuPresenter.BrushMinSizeCellsSettingId),
                settings.Get(ZoningMenuPresenter.BrushMaxSizeCellsSettingId));
        }
    }
}
