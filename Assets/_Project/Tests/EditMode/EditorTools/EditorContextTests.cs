using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.5/2.6: the editor context, which builds the block registry, settings, event definitions and content Ids the same way the game does, without running a simulation.</summary>
    public class EditorContextTests
    {
        private sealed class TestOnlyBlockType : BlockType
        {
            public override string Id => "block.test_only.marker";
            public override string DisplayName => "Test Only Marker";
            public override string Category => "Test";
            public override BlockKind Kind => BlockKind.Action;
            public override IReadOnlyList<PortDefinition> Ports { get; } = new[]
            {
                new PortDefinition("exec", PortDirection.Input, PortKind.Execution),
                new PortDefinition("then", PortDirection.Output, PortKind.Execution)
            };
            public override IReadOnlyList<FieldDefinition> Fields { get; } = Array.Empty<FieldDefinition>();
        }

        private sealed class TestBlockModule : IModule, IModuleDeclaration
        {
            public string Id => "module.editor_catalog_test";
            public int Version => 1;
            public IReadOnlyList<string> Dependencies => Array.Empty<string>();

            public void Declare(ModuleDeclarationContext context) => context.Blocks.Register(new TestOnlyBlockType());
            public void Initialize(ModuleContext context)
            {
            }

            public void Shutdown()
            {
            }
        }

        private static string ManifestJson(bool enabled) => $@"{{
            ""type"": ""module_manifest"",
            ""items"": [ {{ ""id"": ""module.editor_catalog_test"", ""version"": 1, ""dependencies"": [], ""enabled"": {(enabled ? "true" : "false")} }} ]
        }}";

        [Test]
        public void Build_ContainsCoreBlocks_AndAnEnabledTestModulesBlocks()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", ManifestJson(enabled: true));

            EditorContext context = EditorContext.Build(new[] { source.FolderPath }, new IModule[] { new TestBlockModule() });

            Assert.IsTrue(context.Blocks.TryGet("block.flow.wait", out _), "should contain core blocks.");
            Assert.IsTrue(context.Blocks.TryGet("block.flow.ask_player", out _), "should contain core blocks.");
            Assert.IsTrue(context.Blocks.TryGet("block.test_only.marker", out _), "should contain the enabled test module's blocks.");
        }

        [Test]
        public void Build_ExcludesBlocksFromASwitchedOffModule()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", ManifestJson(enabled: false));

            EditorContext context = EditorContext.Build(new[] { source.FolderPath }, new IModule[] { new TestBlockModule() });

            Assert.IsTrue(context.Blocks.TryGet("block.flow.wait", out _), "should still contain core blocks.");
            Assert.IsFalse(context.Blocks.TryGet("block.test_only.marker", out _), "should not contain a switched-off module's blocks.");
        }

        [Test]
        public void Build_IndexesContentIdsByType_AndRegistersEventDefinitionsAndSettingsFromContent()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", @"{
                ""type"": ""module_manifest"",
                ""items"": []
            }");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""id"": ""test.sample_event"", ""visibility"": ""Public"", ""reportingScore"": 0, ""notabilityScore"": 0, ""description"": ""x"" }
                ]
            }");
            source.WriteDataFile("settings.json", @"{
                ""type"": ""setting_definition"",
                ""items"": [
                    { ""id"": ""setting.rules.max_fires_per_tick"", ""valueType"": ""Integer"", ""default"": 1000, ""minimum"": 1, ""maximum"": 1000000, ""category"": ""Rules"", ""description"": ""x"" },
                    { ""id"": ""setting.test.sample"", ""valueType"": ""Number"", ""default"": 1, ""minimum"": 0, ""maximum"": 10, ""category"": ""Test"", ""description"": ""x"" }
                ]
            }");

            EditorContext context = EditorContext.Build(new[] { source.FolderPath }, Array.Empty<IModule>());

            Assert.IsTrue(context.Events.IsRegistered("test.sample_event"), "events from content should be registered.");
            Assert.IsTrue(context.Events.IsRegistered(NewDayEvent.DefinitionIdValue), "the core's own code-registered events should be present too.");
            Assert.IsTrue(context.Settings.IsDeclared("setting.test.sample"), "settings from content should be declared.");

            Assert.IsTrue(context.ContentIdsByType.TryGetValue("event_definition", out IReadOnlyList<string> eventIds));
            CollectionAssert.Contains(eventIds, "test.sample_event");
        }

        [Test]
        public void Build_AgainstTheRealCoreContentFolder_WithTheRealModuleCatalog_Succeeds()
        {
            string coreFolder = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "Content", "core");

            EditorContext context = null;
            Assert.DoesNotThrow(() => context = EditorContext.Build(new[] { coreFolder }));

            Assert.IsTrue(context.Blocks.TryGet("block.flow.wait", out _));
            Assert.IsTrue(context.Blocks.TryGet("block.flow.ask_player", out _));
            Assert.IsTrue(context.ContentIdsByType.ContainsKey(BuiltInContentTypes.RuleTypeName));
        }
    }
}
