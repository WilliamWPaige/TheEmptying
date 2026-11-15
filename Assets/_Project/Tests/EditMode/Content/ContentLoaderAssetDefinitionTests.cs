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
    /// </summary>
    public class ContentLoaderAssetDefinitionTests
    {
        private static ContentTypeRegistry CreateBuiltInTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            return registry;
        }

        private static ContentAssetDefinition GoodAssetDefinition(string id, int basePriceHalfShil = 5)
        {
            var good = new Good(id, "Test Good", GoodCategory.Raw, basePriceHalfShil);
            return new ContentAssetDefinition(BuiltInContentTypes.GoodTypeName, good, "content-assets", id);
        }

        [Test]
        public void AssetDefinitions_LoadIntoTheDatabase_AlongsideJsonSources()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("events.json", @"{
                ""type"": ""event_definition"",
                ""items"": [ { ""id"": ""event.from_json"", ""visibility"": ""Public"", ""reportingScore"": 1, ""notabilityScore"": 1, ""description"": ""x"" } ]
            }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Development);
            var assets = new[] { GoodAssetDefinition("good.log") };

            (ContentDatabase database, ContentLoadReport report) = loader.Load(new[] { source.FolderPath }, assetDefinitions: assets);

            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(database.TryGet(BuiltInContentTypes.EventDefinitionTypeName, "event.from_json", out _));
            Assert.IsTrue(database.TryGet(BuiltInContentTypes.GoodTypeName, "good.log", out ContentEntry entry));
            Assert.AreEqual("content-assets", entry.SourceId);
        }

        [Test]
        public void DuplicateIdBetweenTwoAssets_IsReported()
        {
            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            var assets = new[] { GoodAssetDefinition("good.log"), GoodAssetDefinition("good.log") };

            (ContentDatabase database, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "good.log" && p.Field == "id"));
            Assert.IsTrue(database.TryGet(BuiltInContentTypes.GoodTypeName, "good.log", out _));
        }

        [Test]
        public void DuplicateIdBetweenAnAssetAndJson_IsReported()
        {
            using var source = new TempContentSource("core");
            source.WriteDataFile("goods.json", @"{
                ""type"": ""good"",
                ""items"": [ { ""id"": ""good.log"", ""name"": ""Log"", ""category"": ""Raw"", ""basePriceHalfShil"": 5 } ]
            }");

            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Release);
            var assets = new[] { GoodAssetDefinition("good.log", basePriceHalfShil: 999) };

            (ContentDatabase database, ContentLoadReport report) = loader.Load(new[] { source.FolderPath }, assetDefinitions: assets);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "good.log" && p.Field == "id"));

            // Release mode keeps the first-loaded copy (the JSON one) rather than the duplicate.
            ContentEntry entry = database.Get(BuiltInContentTypes.GoodTypeName, "good.log");
            Assert.AreEqual(5, ((Good)entry.Definition).BasePriceHalfShil);
        }

        [Test]
        public void DevelopmentMode_DuplicateAsset_ThrowsWithFullList()
        {
            var loader = new ContentLoader(CreateBuiltInTypes(), ErrorMode.Development);
            var assets = new[] { GoodAssetDefinition("good.log"), GoodAssetDefinition("good.log") };

            ContentValidationException ex = Assert.Throws<ContentValidationException>(() => loader.Load(null, assetDefinitions: assets));

            Assert.AreEqual(1, ex.Report.Problems.Count);
        }
    }
}
