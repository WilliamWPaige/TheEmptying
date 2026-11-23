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
