using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>
    /// Covers the "alongside the JSON sources" path of ContentLoader: records
    /// built directly in code (no ScriptableObjects at all), merged with JSON
    /// content, with a duplicate Id between either source treated as an error.
    /// Uses a test-only content type, since this path doesn't need a real one.
    /// </summary>
    public class ContentLoaderAssetDefinitionTests
    {
        private const string WidgetTypeName = "test.widget";

        private sealed class Widget : ContentDefinition
        {
            public int Value { get; set; }
        }

        private static ContentTypeRegistry CreateTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            registry.Register(new ContentTypeDefinition(WidgetTypeName, typeof(Widget)));
            return registry;
        }

        private static ContentAssetDefinition WidgetAssetDefinition(string id, int value = 500)
        {
            var widget = new Widget { Id = id, Value = value };
            return new ContentAssetDefinition(WidgetTypeName, widget, "content-assets", id);
        }

        [Test]
        public void AssetDefinitions_LoadIntoTheDatabase_AlongsideJsonSources()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [ { ""id"": ""event.from_json"", ""visibility"": ""Public"", ""reportingScore"": 1, ""notabilityScore"": 1, ""description"": ""x"" } ]
            }");

            var loader = new ContentLoader(CreateTypes(), ErrorMode.Development);
            var assets = new[] { WidgetAssetDefinition("test.widget_one") };

            (ContentDatabase database, ContentLoadReport report) = loader.Load(new[] { source.FolderPath }, assetDefinitions: assets);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(database.TryGet(BuiltInContentTypes.EventDefinitionTypeName, "event.from_json", out _));
            Assert.IsTrue(database.TryGet(WidgetTypeName, "test.widget_one", out ContentEntry entry));
            Assert.AreEqual("content-assets", entry.SourceId);
        }

        [Test]
        public void DuplicateIdBetweenTwoAssets_IsReported()
        {
            var loader = new ContentLoader(CreateTypes(), ErrorMode.Release);
            var assets = new[] { WidgetAssetDefinition("test.widget_one"), WidgetAssetDefinition("test.widget_one") };

            (ContentDatabase database, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "test.widget_one" && p.Field == "id"));
            Assert.IsTrue(database.TryGet(WidgetTypeName, "test.widget_one", out _));
        }

        [Test]
        public void DuplicateIdBetweenAnAssetAndJson_IsReported()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("widgets.json", @"{
                ""type"": ""test.widget"",
                ""items"": [ { ""id"": ""test.widget_one"", ""value"": 500 } ]
            }");

            var loader = new ContentLoader(CreateTypes(), ErrorMode.Release);
            var assets = new[] { WidgetAssetDefinition("test.widget_one", value: 99900) };

            (ContentDatabase database, ContentLoadReport report) = loader.Load(new[] { source.FolderPath }, assetDefinitions: assets);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "test.widget_one" && p.Field == "id"));

            // Release mode keeps the first-loaded copy (the JSON one) rather than the duplicate.
            ContentEntry entry = database.Get(WidgetTypeName, "test.widget_one");
            Assert.AreEqual(500, ((Widget)entry.Definition).Value);
        }

        [Test]
        public void DevelopmentMode_DuplicateAsset_ThrowsWithFullList()
        {
            var loader = new ContentLoader(CreateTypes(), ErrorMode.Development);
            var assets = new[] { WidgetAssetDefinition("test.widget_one"), WidgetAssetDefinition("test.widget_one") };

            ContentValidationException ex = Assert.Throws<ContentValidationException>(() => loader.Load(null, assetDefinitions: assets));

            Assert.AreEqual(1, ex.Report.Problems.Count);
        }
    }
}
