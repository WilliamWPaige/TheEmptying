using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;

namespace TheEmptying.Tests.EditMode.Content
{
    public class SettingsRegistryTests
    {
        [Test]
        public void Set_ClampsToRange_AndReportsTheClamp()
        {
            var registry = new SettingsRegistry();
            registry.Declare(new SettingDefinition
            {
                Id = "setting.raid_strength",
                ValueType = SettingValueType.Number,
                Default = 1.0,
                Minimum = 0.0,
                Maximum = 2.0,
                Category = "Difficulty",
                Description = "test"
            });

            SettingsRegistry.SetResult overResult = registry.Set("setting.raid_strength", 5.0);
            Assert.AreEqual(2.0, overResult.Value);
            Assert.IsTrue(overResult.WasClamped);
            Assert.AreEqual(2.0, registry.Get("setting.raid_strength"));

            SettingsRegistry.SetResult inRangeResult = registry.Set("setting.raid_strength", 1.5);
            Assert.AreEqual(1.5, inRangeResult.Value);
            Assert.IsFalse(inRangeResult.WasClamped);
        }

        [Test]
        public void ExportAll_ListsEverySetting_WithRangeAndCategory()
        {
            var registry = new SettingsRegistry();
            registry.Declare(new SettingDefinition { Id = "setting.b", ValueType = SettingValueType.Boolean, Default = 0, Minimum = 0, Maximum = 1, Category = "Cat1", Description = "d1" });
            registry.Declare(new SettingDefinition { Id = "setting.a", ValueType = SettingValueType.Integer, Default = 5, Minimum = 0, Maximum = 10, Category = "Cat2", Description = "d2" });

            var exported = registry.ExportAll().ToList();

            CollectionAssert.AreEqual(new[] { "setting.a", "setting.b" }, exported.Select(s => s.Id).ToList());

            SettingDefinition a = exported.First(s => s.Id == "setting.a");
            Assert.AreEqual(0, a.Minimum);
            Assert.AreEqual(10, a.Maximum);
            Assert.AreEqual("Cat2", a.Category);
        }
    }
}
