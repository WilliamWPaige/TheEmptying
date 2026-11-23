using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Presentation.Content;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>R7: the real resource/item/unit content types (replacing the R5/R6b examples) load and validate cleanly.</summary>
    public class RealContentTypesTests
    {
        private static ContentCatalog LoadCatalog()
        {
            var catalog = Resources.Load<ContentCatalog>("ContentCatalog");
            Assert.IsNotNull(catalog, "Expected the shipped ContentCatalog to load.");
            return catalog;
        }

        [Test]
        public void ShippedCatalog_HasExactlyTheRealTypes_AndNoExamples()
        {
            ContentCatalog catalog = LoadCatalog();

            CollectionAssert.AreEquivalent(new[] { "unit.person" }, catalog.Units.Select(a => a.Id));
            CollectionAssert.AreEquivalent(new[] { "resource.tree", "resource.stone", "resource.grub_mound" }, catalog.ResourceNodes.Select(a => a.Id));
            CollectionAssert.AreEquivalent(new[] { "item.log", "item.stone", "item.grub" }, catalog.Items.Select(a => a.Id));
            CollectionAssert.AreEquivalent(new[] { "building.keep" }, catalog.Buildings.Select(a => a.Id));
        }

        [Test]
        public void BuildingKeep_Has12x12Footprint()
        {
            ContentCatalog catalog = LoadCatalog();
            BuildingAsset keep = catalog.Buildings.Single(a => a.Id == "building.keep");

            Assert.AreEqual(12, keep.FootprintWidth);
            Assert.AreEqual(12, keep.FootprintDepth);
        }

        [Test]
        public void ResourceStone_HasA2x2Footprint()
        {
            ContentCatalog catalog = LoadCatalog();
            ResourceAsset stone = catalog.ResourceNodes.Single(a => a.Id == "resource.stone");

            Assert.AreEqual(2, stone.FootprintWidth);
            Assert.AreEqual(2, stone.FootprintDepth);
        }

        [Test]
        public void ResourceTreeAndGrubMound_Have1x1Footprints()
        {
            ContentCatalog catalog = LoadCatalog();

            ResourceAsset tree = catalog.ResourceNodes.Single(a => a.Id == "resource.tree");
            ResourceAsset grubMound = catalog.ResourceNodes.Single(a => a.Id == "resource.grub_mound");

            Assert.AreEqual(1, tree.FootprintWidth);
            Assert.AreEqual(1, tree.FootprintDepth);
            Assert.AreEqual(1, grubMound.FootprintWidth);
            Assert.AreEqual(1, grubMound.FootprintDepth);
        }

        /// <summary>R12: the real item fields match the prompt's placeholder values.</summary>
        [TestCase("item.grub", 1, 25.0, 5)]
        [TestCase("item.log", 4, 0.0, 5)]
        [TestCase("item.stone", 6, 0.0, 5)]
        public void RealItems_HaveTheExpectedPlaceholderFields(string itemId, int expectedSize, double expectedFoodValue, int expectedCarryAmount)
        {
            ContentCatalog catalog = LoadCatalog();
            ItemAsset item = catalog.Items.Single(a => a.Id == itemId);

            Assert.AreEqual(expectedSize, item.Size);
            Assert.AreEqual(expectedFoodValue, item.FoodValue);
            Assert.AreEqual(expectedCarryAmount, item.CarryAmount);
        }

        /// <summary>R12: the real resources yield the expected item, hold the expected amount (or are unlimited), and harvest at 2 h / 1 h / 30 min.</summary>
        [TestCase("resource.grub_mound", "item.grub", true, 0)]
        [TestCase("resource.tree", "item.log", false, 5)]
        [TestCase("resource.stone", "item.stone", false, 300)]
        public void RealResources_YieldTheExpectedItemAndAmount(string resourceId, string expectedItemId, bool expectedUnlimited, int expectedAmountHeld)
        {
            ContentCatalog catalog = LoadCatalog();
            ResourceAsset resource = catalog.ResourceNodes.Single(a => a.Id == resourceId);

            Assert.AreEqual(expectedItemId, resource.YieldedItemId);
            Assert.AreEqual(expectedUnlimited, resource.UnlimitedAmount);
            if (!expectedUnlimited)
            {
                Assert.AreEqual(expectedAmountHeld, resource.AmountHeld);
            }

            Assert.AreEqual(120, resource.HarvestTimeUnskilledMinutes);
            Assert.AreEqual(60, resource.HarvestTimeSkilledMinutes);
            Assert.AreEqual(30, resource.HarvestTimeMasterMinutes);
        }

        [Test]
        public void EveryRealAsset_ValidatesCleanly()
        {
            ContentCatalog catalog = LoadCatalog();

            IEnumerable<ContentAssetBase> all = catalog.Units.Cast<ContentAssetBase>()
                .Concat(catalog.ResourceNodes.Cast<ContentAssetBase>())
                .Concat(catalog.Items.Cast<ContentAssetBase>())
                .Concat(catalog.Buildings.Cast<ContentAssetBase>());

            foreach (ContentAssetBase asset in all)
            {
                CollectionAssert.IsEmpty(asset.ValidateSelf(), $"{asset.Id} should have no validation problems.");
            }
        }

        [Test]
        public void BuildDefinitions_ConvertsTheRealCatalog_WithNoProblems()
        {
            ContentCatalog catalog = LoadCatalog();
            var problems = new List<ContentValidationProblem>();

            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(catalog, problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(8, definitions.Count);
        }
    }
}
