using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Startup;
using TheEmptying.Presentation;
using TheEmptying.Tests.EditMode.Startup;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R6b: the shipped day-length setting (time_settings.json) loads with the right default, range and category.</summary>
    public class TimeSettingsTests
    {
        private const string RealSecondsPerDaySettingId = "setting.time.real_seconds_per_day";

        [Test]
        public void RealSecondsPerDay_LoadsFromRealContent_WithExpectedDefaultRangeAndCategory()
        {
            using var core = new TempCoreContentCopy();
            IModule[] modules = ModuleCatalog.CreateModules();
            (Simulation simulation, StartupReport _) = SimulationBuilder.Build(1, new[] { core.FolderPath }, modules);

            Assert.IsTrue(simulation.Settings.TryGetDefinition(RealSecondsPerDaySettingId, out SettingDefinition definition));
            Assert.AreEqual(SettingValueType.Number, definition.ValueType);
            Assert.AreEqual(1800.0, definition.Default);
            Assert.AreEqual(60.0, definition.Minimum);
            Assert.AreEqual(7200.0, definition.Maximum);
            Assert.AreEqual("Time", definition.Category);
            Assert.AreEqual(1800.0, simulation.Settings.Get(RealSecondsPerDaySettingId), "The live value should start at the default (30 real minutes per in-game day).");
        }
    }
}
