using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Presentation.Content;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>Converting a catalog's ScriptableObject content into records: ordinal Id order and repeatability.</summary>
    public class ContentAssetConverterTests
    {
        private static GoodAsset MakeGood(string id, string name, float basePriceShil)
        {
            var asset = ScriptableObject.CreateInstance<GoodAsset>();
            asset.SetForEditor(id, name, GoodCategory.Raw, basePriceShil, string.Empty);
            return asset;
        }

        private static ContentCatalog MakeCatalog(List<GoodAsset> goods)
        {
            var catalog = ScriptableObject.CreateInstance<ContentCatalog>();
            catalog.SetContentsForEditor(goods, new List<NeedAsset>(), new List<ZoneAsset>(), new List<BuildingTypeAsset>(), new List<RecipeAsset>());
            return catalog;
        }

        [Test]
        public void BuildDefinitions_SortsEachTypeByIdInOrdinalOrder()
        {
            var goods = new List<GoodAsset>
            {
                MakeGood("good.wheat", "Wheat", 1),
                MakeGood("good.log", "Log", 5),
                MakeGood("good.block", "Block", 20)
            };

            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(MakeCatalog(goods));

            CollectionAssert.AreEqual(
                new[] { "good.block", "good.log", "good.wheat" },
                definitions.ConvertAll(d => d.Definition.Id));
        }

        [Test]
        public void BuildDefinitions_CalledTwiceOnTheSameAssets_GivesEqualRecords()
        {
            ContentCatalog catalog = MakeCatalog(new List<GoodAsset> { MakeGood("good.log", "Log", 5) });

            List<ContentAssetDefinition> first = ContentAssetConverter.BuildDefinitions(catalog);
            List<ContentAssetDefinition> second = ContentAssetConverter.BuildDefinitions(catalog);

            var a = (Good)first[0].Definition;
            var b = (Good)second[0].Definition;

            Assert.AreEqual(a.Id, b.Id);
            Assert.AreEqual(a.Name, b.Name);
            Assert.AreEqual(a.Category, b.Category);
            Assert.AreEqual(a.BasePrice, b.BasePrice);
        }

        [Test]
        public void BuildDefinitions_SkipsANullEntryInTheCatalog()
        {
            var goods = new List<GoodAsset> { MakeGood("good.log", "Log", 5), null };

            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(MakeCatalog(goods));

            Assert.AreEqual(1, definitions.Count);
        }
    }
}
