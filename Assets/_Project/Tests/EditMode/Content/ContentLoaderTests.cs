using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Scheduling;

namespace TheEmptying.Tests.EditMode.Content
{
    public class ContentLoaderTests
    {
        private sealed class RefTargetContent : ContentDefinition
        {
        }

        private sealed class RefSourceContent : ContentDefinition
        {
            [JsonProperty("target")]
            public ContentRef<RefTargetContent> Target { get; set; }
        }

        private sealed class WidgetDeclaringModule : IModule, IModuleDeclaration
        {
            public string Id => "test.widgets";
            public int Version => 1;
            public System.Collections.Generic.IReadOnlyList<string> Dependencies { get; } = Array.Empty<string>();

            public void Declare(ModuleDeclarationContext context)
            {
                context.ContentTypes.Register(new ContentTypeDefinition("custom_widget", typeof(RefTargetContent)));
            }

            public void Initialize(ModuleContext context)
            {
            }

            public void Shutdown()
            {
            }
        }

        private static ContentTypeRegistry CreateBuiltInTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            return registry;
        }

        [Test]
        public void MissingId_IsReportedWithSourceFileItemAndField()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [ { ""visibility"": ""Public"", ""reportingScore"": 1, ""notabilityScore"": 1, ""description"": ""no id"" } ]
            }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            (_, ContentLoadReport report) = loader.Load(new[] { source.FolderPath });

            Assert.IsTrue(report.HasErrors);
            ContentValidationProblem problem = report.Problems.Single();
            Assert.AreEqual("core", problem.SourceId);
            Assert.AreEqual("events.json", problem.File);
            Assert.AreEqual("id", problem.Field);
        }

        [Test]
        public void OutOfRangeValue_IsReported()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [ { ""id"": ""event.bad"", ""visibility"": ""Public"", ""reportingScore"": 20, ""notabilityScore"": 1, ""description"": ""x"" } ]
            }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            (_, ContentLoadReport report) = loader.Load(new[] { source.FolderPath });

            Assert.IsTrue(report.HasErrors);
            ContentValidationProblem problem = report.Problems.Single(p => p.ItemId == "event.bad");
            Assert.AreEqual("core", problem.SourceId);
            Assert.AreEqual("events.json", problem.File);
            Assert.AreEqual("reportingScore", problem.Field);
        }

        [Test]
        public void UnknownTypeName_IsReported()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("odd.json", @"{ ""type"": ""not_a_real_type"", ""items"": [] }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            (_, ContentLoadReport report) = loader.Load(new[] { source.FolderPath });

            Assert.IsTrue(report.HasErrors);
            ContentValidationProblem problem = report.Problems.Single();
            Assert.AreEqual("core", problem.SourceId);
            Assert.AreEqual("odd.json", problem.File);
            Assert.AreEqual("type", problem.Field);
        }

        [Test]
        public void MalformedFile_IsReported()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("broken.json", "{ this is not valid json ");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            (_, ContentLoadReport report) = loader.Load(new[] { source.FolderPath });

            Assert.IsTrue(report.HasErrors);
            ContentValidationProblem problem = report.Problems.Single();
            Assert.AreEqual("core", problem.SourceId);
            Assert.AreEqual("broken.json", problem.File);
        }

        [Test]
        public void BadReference_IsReported()
        {
            using var source = new TempContentSource("core");
            ContentTypeRegistry types = CreateBuiltInTypes();
            types.Register(new ContentTypeDefinition("ref_source", typeof(RefSourceContent)));
            types.Register(new ContentTypeDefinition("ref_target", typeof(RefTargetContent)));

            source.WriteDataFile("refs.json", @"{
                ""type"": ""ref_source"",
                ""items"": [ { ""id"": ""ref.one"", ""target"": { ""id"": ""ref_target.missing"" } } ]
            }");

            var loader = new ContentLoader(types, ErrorMode.Release);
            (_, ContentLoadReport report) = loader.Load(new[] { source.FolderPath });

            Assert.IsTrue(report.HasErrors);
            ContentValidationProblem problem = report.Problems.Single();
            Assert.AreEqual("core", problem.SourceId);
            Assert.AreEqual("refs.json", problem.File);
            Assert.AreEqual("ref.one", problem.ItemId);
            Assert.AreEqual("Target", problem.Field);
        }

        [Test]
        public void DevelopmentMode_AnyError_StopsLoadingWithFullList()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""visibility"": ""Public"", ""reportingScore"": 1, ""notabilityScore"": 1, ""description"": ""missing id"" },
                    { ""id"": ""event.bad"", ""visibility"": ""Public"", ""reportingScore"": 99, ""notabilityScore"": 1, ""description"": ""bad score"" }
                ]
            }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Development);

            ContentValidationException ex = Assert.Throws<ContentValidationException>(() => loader.Load(new[] { source.FolderPath }));

            Assert.AreEqual(2, ex.Report.Problems.Count);
        }

        [Test]
        public void ReleaseMode_SkipsBadItems_AndKeepsReport()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [
                    { ""id"": ""event.good"", ""visibility"": ""Public"", ""reportingScore"": 1, ""notabilityScore"": 1, ""description"": ""ok"" },
                    { ""id"": ""event.bad"", ""visibility"": ""Public"", ""reportingScore"": 99, ""notabilityScore"": 1, ""description"": ""bad score"" }
                ]
            }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            (ContentDatabase database, ContentLoadReport report) = loader.Load(new[] { source.FolderPath });

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(database.TryGet(BuiltInContentTypes.EventDefinitionTypeName, "event.good", out _));
            Assert.IsFalse(database.TryGet(BuiltInContentTypes.EventDefinitionTypeName, "event.bad", out _));
        }

        [Test]
        public void ModuleRegisteredContentTypeDuringDeclaration_LoadsItemsFromAMod()
        {
            using var coreSource = new TempContentSource("core");
            using var modSource = new TempContentSource("modA");

            modSource.WriteDataFile("widgets.json", @"{
                ""type"": ""custom_widget"",
                ""items"": [ { ""id"": ""widget.one"" } ]
            }");

            var definitions = new EventDefinitionRegistry();
            var events = new EventBus(definitions);
            var scheduler = new Scheduler();
            var entities = new EntityRegistry();
            var random = new RandomService(1);
            long tick = 0;
            var host = new ModuleHost(events, scheduler, entities, definitions, random, () => tick);

            var module = new WidgetDeclaringModule();
            host.ValidateAndOrder(new[] { new ModuleRegistration(module, new ModuleManifest(module.Id, 1, Array.Empty<string>(), true)) });

            ContentTypeRegistry contentTypes = CreateBuiltInTypes();
            var settings = new SettingsRegistry();
            host.RunDeclarationPhase(contentTypes, settings);

            var loader = new ContentLoader(contentTypes, ErrorMode.Development);
            (ContentDatabase database, ContentLoadReport report) = loader.Load(new[] { coreSource.FolderPath, modSource.FolderPath });

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(database.TryGet("custom_widget", "widget.one", out ContentEntry entry));
            Assert.AreEqual("modA", entry.SourceId);

            host.InitializeModules();
        }

        [Test]
        public void EventDefinitionsLoadedFromData_CanBePostedOnTheEventBus()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [ { ""id"": ""event.from_data"", ""visibility"": ""Public"", ""reportingScore"": 3, ""notabilityScore"": 2, ""description"": ""test"" } ]
            }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Development);
            (ContentDatabase database, _) = loader.Load(new[] { source.FolderPath });

            var definitions = new EventDefinitionRegistry();
            BuiltInContentTypes.RegisterEventDefinitionsFrom(database, definitions);

            var bus = new EventBus(definitions);
            Assert.DoesNotThrow(() => bus.Post("event.from_data", 0));
        }

        [Test]
        public void MissingModuleManifest_IsAnError()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("empty.json", @"{ ""type"": ""event_definition"", ""items"": [] }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            (_, ContentLoadReport report) = loader.Load(new[] { source.FolderPath }, new[] { "module.needs" });

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "module.needs"));
        }
    }
}
