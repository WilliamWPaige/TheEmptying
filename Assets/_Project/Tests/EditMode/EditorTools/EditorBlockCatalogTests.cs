using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Rules;
using TheEmptying.Editor.Rules;
using TheEmptying.Tests.EditMode.Content;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.5: the editor-side block catalog, which builds the full BlockRegistry the same way the game does, without running a simulation.</summary>
    public class EditorBlockCatalogTests
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

            BlockRegistry registry = EditorBlockCatalog.Build(new[] { source.FolderPath }, new IModule[] { new TestBlockModule() });

            Assert.IsTrue(registry.TryGet("block.flow.wait", out _), "should contain core blocks.");
            Assert.IsTrue(registry.TryGet("block.flow.ask_player", out _), "should contain core blocks.");
            Assert.IsTrue(registry.TryGet("block.test_only.marker", out _), "should contain the enabled test module's blocks.");
        }

        [Test]
        public void Build_AgainstTheRealCoreContentFolder_WithTheRealModuleCatalog_Succeeds()
        {
            string coreFolder = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "Content", "core");

            BlockRegistry registry = null;
            Assert.DoesNotThrow(() => registry = EditorBlockCatalog.Build(new[] { coreFolder }));

            Assert.IsTrue(registry.TryGet("block.flow.wait", out _));
            Assert.IsTrue(registry.TryGet("block.flow.ask_player", out _));
        }

        [Test]
        public void Build_ExcludesBlocksFromASwitchedOffModule()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("manifest.json", ManifestJson(enabled: false));

            BlockRegistry registry = EditorBlockCatalog.Build(new[] { source.FolderPath }, new IModule[] { new TestBlockModule() });

            Assert.IsTrue(registry.TryGet("block.flow.wait", out _), "should still contain core blocks.");
            Assert.IsFalse(registry.TryGet("block.test_only.marker", out _), "should not contain a switched-off module's blocks.");
        }
    }
}
