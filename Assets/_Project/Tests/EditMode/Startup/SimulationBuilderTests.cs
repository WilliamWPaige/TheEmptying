using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TheEmptying.Core;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Startup;
using TheEmptying.Tests.EditMode.Content;
using TheEmptying.Tests.EditMode.Saves;

namespace TheEmptying.Tests.EditMode.Startup
{
    public class SimulationBuilderTests
    {
        [Test]
        public void Build_FromSources_MakesContentDefinedEventDefinitionsAndSettingsAvailable()
        {
            using var core = new TempContentSource("core");
            core.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""id"": ""test.thing_happened"", ""visibility"": ""Public"", ""reportingScore"": 1, ""notabilityScore"": 1, ""description"": ""A thing happened."" }
                ]
            }");
            core.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""test.some_rate"", ""valueType"": ""Number"", ""default"": 1.5, ""minimum"": 0, ""maximum"": 10, ""category"": ""Test"", ""description"": ""A test rate."" }
                ]
            }");

            (Simulation simulation, StartupReport report) = SimulationBuilder.Build(1, new[] { core.FolderPath }, null);

            Assert.IsTrue(simulation.EventDefinitions.IsRegistered("test.thing_happened"));
            Assert.IsTrue(simulation.Settings.IsDeclared("test.some_rate"));
            Assert.AreEqual(1.5, simulation.Settings.Get("test.some_rate"));
            Assert.AreEqual(0, report.ContentProblems.Count);
        }

        [Test]
        public void Build_ThenSaveAndLoadThroughTheBuilder_StateHashMatches()
        {
            using var core = new TempContentSource("core");
            core.WriteDataFile("manifests.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [
                    { ""id"": ""test.counter"", ""version"": 1, ""dependencies"": [], ""enabled"": true }
                ]
            }");
            string[] sources = { core.FolderPath };

            var counter = new CounterModule();
            (Simulation built, _) = SimulationBuilder.Build(42, sources, new IModule[] { counter });

            for (int i = 0; i < 25; i++)
            {
                built.Step();
            }

            ulong originalHash = built.ComputeStateHash();

            using var stream = new MemoryStream();
            GameSave.Save(built, new SaveHeaderInfo { KingdomName = "K", Population = 1 }, stream);
            stream.Position = 0;

            var loadedCounter = new CounterModule();
            (Simulation loaded, _) = GameSave.Load(stream, sources, new IModule[] { loadedCounter });

            Assert.AreEqual(originalHash, loaded.ComputeStateHash());
        }

        [Test]
        public void Build_ModSwitchingAModuleOff_IsReflectedInTheBuild()
        {
            using var core = new TempContentSource("core");
            core.WriteDataFile("manifests.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [
                    { ""id"": ""test.counter"", ""version"": 1, ""dependencies"": [], ""enabled"": true }
                ]
            }");

            using var mod = new TempContentSource("modA");
            mod.WriteDataFile("manifests.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [
                    { ""id"": ""test.counter"", ""version"": 1, ""dependencies"": [], ""enabled"": false }
                ]
            }");

            var counter = new CounterModule();
            (Simulation simulation, StartupReport report) = SimulationBuilder.Build(1, new[] { core.FolderPath, mod.FolderPath }, new IModule[] { counter });

            Assert.IsFalse(simulation.Modules.IsActive("test.counter"));
            Assert.IsTrue(report.DisabledModuleReasons.Count > 0);
        }
    }
}
