using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Presentation.Content;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>The shipped starting content (Data/Resources/ContentCatalog and the assets it lists) validates clean.</summary>
    public class StartingContentTests
    {
        private static ContentTypeRegistry CreateTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            return registry;
        }

        [Test]
        public void StartingContentCatalog_IsPresentInResources()
        {
            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            Assert.IsNotNull(catalog, "Expected Assets/_Project/Data/Resources/ContentCatalog.asset.");
            Assert.AreEqual(8, catalog.Goods.Count);
            Assert.AreEqual(12, catalog.Needs.Count);
            Assert.AreEqual(6, catalog.Zones.Count);
            Assert.AreEqual(13, catalog.BuildingTypes.Count);
            Assert.AreEqual(8, catalog.Recipes.Count);
        }

        [Test]
        public void StartingContent_ValidatesClean()
        {
            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            Assert.IsNotNull(catalog);

            foreach (GoodAsset asset in catalog.Goods) Assert.IsFalse(asset.ValidateSelf().Any(), $"{asset.name}: {string.Join("; ", asset.ValidateSelf())}");
            foreach (NeedAsset asset in catalog.Needs) Assert.IsFalse(asset.ValidateSelf().Any(), $"{asset.name}: {string.Join("; ", asset.ValidateSelf())}");
            foreach (ZoneAsset asset in catalog.Zones) Assert.IsFalse(asset.ValidateSelf().Any(), $"{asset.name}: {string.Join("; ", asset.ValidateSelf())}");
            foreach (BuildingTypeAsset asset in catalog.BuildingTypes) Assert.IsFalse(asset.ValidateSelf().Any(), $"{asset.name}: {string.Join("; ", asset.ValidateSelf())}");
            foreach (RecipeAsset asset in catalog.Recipes) Assert.IsFalse(asset.ValidateSelf().Any(), $"{asset.name}: {string.Join("; ", asset.ValidateSelf())}");

            var loader = new ContentLoader(CreateTypes(), ErrorMode.Development);
            (_, ContentLoadReport report) = loader.Load(null, assetDefinitions: ContentAssetConverter.BuildDefinitions(catalog));

            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Problems.Select(p => p.ToString())));
        }

        [Test]
        public void StartingContent_LoadsInOrdinalIdOrder()
        {
            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            Assert.IsNotNull(catalog);

            var goodIds = catalog.Goods.Select(g => g.Id).ToList();
            var sorted = goodIds.OrderBy(id => id, System.StringComparer.Ordinal).ToList();

            var definitions = ContentAssetConverter.BuildDefinitions(catalog)
                .Where(d => d.TypeName == BuiltInContentTypes.GoodTypeName)
                .Select(d => d.Definition.Id)
                .ToList();

            CollectionAssert.AreEqual(sorted, definitions);
        }
    }
}
