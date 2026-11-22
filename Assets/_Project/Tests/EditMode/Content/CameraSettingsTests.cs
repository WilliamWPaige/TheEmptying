using System;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Presentation.World;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R1: the shipped camera settings (camera_settings.json) load with the right defaults, ranges and categories.</summary>
    public class CameraSettingsTests
    {
        private static SettingsRegistry BuildFromRealContent()
        {
            using var core = new TempCoreContentCopy();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, Array.Empty<IModule>());
            return simulation.Settings;
        }

        [TestCase(StrategyCameraController.PanSpeedSettingId, SettingValueType.Number, 10.0, 1.0, 50.0, "Camera")]
        [TestCase(StrategyCameraController.ZoomSpeedSettingId, SettingValueType.Number, 2.0, 0.1, 20.0, "Camera")]
        [TestCase(StrategyCameraController.ZoomMinHeightSettingId, SettingValueType.Number, 10.0, 1.0, 500.0, "Camera")]
        [TestCase(StrategyCameraController.ZoomMaxHeightSettingId, SettingValueType.Number, 100.0, 1.0, 1000.0, "Camera")]
        [TestCase(StrategyCameraController.RotateSpeedSettingId, SettingValueType.Number, 90.0, 10.0, 360.0, "Camera")]
        [TestCase(StrategyCameraController.RotateDragSensitivitySettingId, SettingValueType.Number, 0.3, 0.01, 5.0, "Camera")]
        [TestCase(StrategyCameraController.EdgeScrollingSettingId, SettingValueType.Boolean, 0.0, 0.0, 1.0, "Camera")]
        [TestCase(StrategyCameraController.EdgeScrollSizeSettingId, SettingValueType.Number, 20.0, 1.0, 200.0, "Camera")]
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
        public void EdgeScrolling_DefaultsOff()
        {
            SettingsRegistry settings = BuildFromRealContent();
            Assert.AreEqual(0.0, settings.Get(StrategyCameraController.EdgeScrollingSettingId));
        }

        [Test]
        public void ZoomMinHeight_IsLessThanZoomMaxHeight()
        {
            SettingsRegistry settings = BuildFromRealContent();
            Assert.Less(settings.Get(StrategyCameraController.ZoomMinHeightSettingId), settings.Get(StrategyCameraController.ZoomMaxHeightSettingId));
        }
    }
}
