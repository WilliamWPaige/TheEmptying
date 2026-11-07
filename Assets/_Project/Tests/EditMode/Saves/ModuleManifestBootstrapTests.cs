using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Scheduling;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.Saves
{
    public class ModuleManifestBootstrapTests
    {
        private sealed class RecordingModule : IModule
        {
            private readonly List<string> _log;

            public string Id { get; }
            public int Version => 1;
            public IReadOnlyList<string> Dependencies { get; }

            public RecordingModule(string id, List<string> log, params string[] dependencies)
            {
                Id = id;
                _log = log;
                Dependencies = dependencies;
            }

            public void Initialize(ModuleContext context) => _log.Add($"init:{Id}");
            public void Shutdown()
            {
            }
        }

        private static ModuleHost CreateHost()
        {
            var definitions = new EventDefinitionRegistry();
            var events = new EventBus(definitions);
            var scheduler = new Scheduler();
            var entities = new EntityRegistry();
            var random = new RandomService(1);
            return new ModuleHost(events, scheduler, entities, definitions, random, () => 0L);
        }

        [Test]
        public void ModsManifestDisablingAModule_DisablesItAndItsDependents()
        {
            using var core = new TempContentSource("core");
            using var mod = new TempContentSource("modA");

            core.WriteDataFile("manifests.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [
                    { ""id"": ""mod.a"", ""version"": 1, ""dependencies"": [], ""enabled"": true },
                    { ""id"": ""mod.b"", ""version"": 1, ""dependencies"": [""mod.a""], ""enabled"": true }
                ]
            }");

            mod.WriteDataFile("manifests.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [
                    { ""id"": ""mod.a"", ""version"": 1, ""dependencies"": [], ""enabled"": false }
                ]
            }");

            Dictionary<string, ModuleManifest> manifests = ModuleManifestBootstrap.LoadManifests(new[] { core.FolderPath, mod.FolderPath });

            Assert.IsFalse(manifests["mod.a"].Enabled);

            var log = new List<string>();
            var moduleA = new RecordingModule("mod.a", log);
            var moduleB = new RecordingModule("mod.b", log, "mod.a");

            ModuleHost host = CreateHost();
            host.ValidateAndOrder(new IModule[] { moduleA, moduleB }, manifests);

            Assert.IsFalse(host.IsActive("mod.a"));
            Assert.IsFalse(host.IsActive("mod.b"));
        }

        [Test]
        public void ManifestWithNoMatchingModuleCode_IsAClearError()
        {
            using var core = new TempContentSource("core");
            core.WriteDataFile("manifests.json", @"{
                ""type"": ""module_manifest"",
                ""items"": [ { ""id"": ""mod.ghost"", ""version"": 1, ""dependencies"": [], ""enabled"": true } ]
            }");

            Dictionary<string, ModuleManifest> manifests = ModuleManifestBootstrap.LoadManifests(new[] { core.FolderPath });

            ModuleHost host = CreateHost();
            ModuleValidationException ex = Assert.Throws<ModuleValidationException>(() => host.ValidateAndOrder(Array.Empty<IModule>(), manifests));

            StringAssert.Contains("mod.ghost", ex.Message);
        }
    }
}
