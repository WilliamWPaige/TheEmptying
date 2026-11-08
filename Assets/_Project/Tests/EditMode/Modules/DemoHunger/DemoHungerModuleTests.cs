using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Modules.DemoHunger;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Modules.DemoHunger
{
    public class DemoHungerModuleTests
    {
        private static TempContentSource BuildSource()
        {
            var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [
                    { ""id"": ""module.demo_hunger"", ""version"": 1, ""dependencies"": [], ""enabled"": true }
                ]
            }");
            source.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""setting.demo_hunger.decay_per_hour"", ""valueType"": ""Number"", ""default"": 2, ""minimum"": 0, ""maximum"": 20, ""category"": ""Demo"", ""description"": ""decay"" }
                ]
            }");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""id"": ""event.demo_hunger.starving"", ""visibility"": ""Witnessed"", ""reportingScore"": 5, ""notabilityScore"": 3, ""description"": ""starving"" }
                ]
            }");
            return source;
        }

        [Test]
        public void NewGame_CreatesFiftyPersonsWithFullHunger()
        {
            using TempContentSource source = BuildSource();
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { source.FolderPath }, new IModule[] { new DemoHungerModule() });

            List<EntityId> persons = sim.Entities.LivingEntities().ToList();
            Assert.AreEqual(50, persons.Count);

            foreach (EntityId id in persons)
            {
                StringAssert.Contains("hunger: 100.0", string.Join("\n", sim.Modules.Inspect(id)));
            }
        }

        [Test]
        public void RunningManyTicks_LowersHunger()
        {
            using TempContentSource source = BuildSource();
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { source.FolderPath }, new IModule[] { new DemoHungerModule() });

            EntityId first = sim.Entities.LivingEntities().First();

            for (int i = 0; i < 2000; i++)
            {
                sim.Step();
            }

            string inspected = string.Join("\n", sim.Modules.Inspect(first));
            StringAssert.IsMatch(@"hunger: \d+\.\d \(High\)", inspected);
            StringAssert.DoesNotContain("hunger: 100.0", inspected);
        }

        [Test]
        public void HungerCrossingBelowThreshold_PostsAStarvingEventForThatEntity()
        {
            using TempContentSource source = BuildSource();
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { source.FolderPath }, new IModule[] { new DemoHungerModule() });

            sim.Settings.Set(DemoHungerModule.DecayPerHourSettingId, 20);

            var starving = new List<EntityId>();
            sim.Events.Subscribe(DemoHungerModule.StarvingEventId, posted => starving.Add(posted.Data.GetEntity("entity")));

            for (int i = 0; i < 3000 && starving.Count == 0; i++)
            {
                sim.Step();
            }

            Assert.Greater(starving.Count, 0, "Expected at least one starving event with a high decay setting.");
            Assert.IsTrue(sim.Entities.IsAlive(starving[0]));
        }

        [Test]
        public void FeedCommand_ResetsHungerToOneHundred()
        {
            using TempContentSource source = BuildSource();
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { source.FolderPath }, new IModule[] { new DemoHungerModule() });

            EntityId first = sim.Entities.LivingEntities().First();

            for (int i = 0; i < 2000; i++)
            {
                sim.Step();
            }

            string result = sim.Commands.Execute($"feed {first}");
            StringAssert.Contains("fed", result);
            StringAssert.Contains("hunger: 100.0", string.Join("\n", sim.Modules.Inspect(first)));
        }

        [Test]
        public void FeedCommand_WithAnUnknownId_PrintsUsage()
        {
            using TempContentSource source = BuildSource();
            (Simulation sim, _) = SimulationBuilder.Build(1, new[] { source.FolderPath }, new IModule[] { new DemoHungerModule() });

            Assert.AreEqual("Usage: feed <entityId> (e.g. e12.3)", sim.Commands.Execute("feed e999.0"));
        }

        [Test]
        public void SaveAndLoad_RestoresExactHungerValues()
        {
            using TempContentSource source = BuildSource();
            string[] sources = { source.FolderPath };

            (Simulation original, _) = SimulationBuilder.Build(1, sources, new IModule[] { new DemoHungerModule() });
            for (int i = 0; i < 1500; i++)
            {
                original.Step();
            }

            ulong originalHash = original.ComputeStateHash();

            using var stream = new MemoryStream();
            GameSave.Save(original, new SaveHeaderInfo { KingdomName = "K", Population = original.Entities.Count }, stream);
            stream.Position = 0;

            (Simulation loaded, _) = GameSave.Load(stream, sources, new IModule[] { new DemoHungerModule() });

            Assert.AreEqual(originalHash, loaded.ComputeStateHash());
            Assert.AreEqual(50, loaded.Entities.Count, "Loading must not re-run new-game setup and create a second batch of people.");
        }
    }
}
