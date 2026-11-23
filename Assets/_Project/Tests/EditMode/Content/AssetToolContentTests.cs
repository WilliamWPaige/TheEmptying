using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Presentation.Content;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>
    /// R5 asset tool: each type's own ValidateSelf() (the same checks the
    /// Inspector, the "Validate All Content" menu and game load all show),
    /// converting a catalog's assets into records, and the same checks
    /// again through the real content pipeline (ContentLoader) under the
    /// registered unit/resource/building/item type names.
    /// </summary>
    public class AssetToolContentTests
    {
        private static GameObject MakeModel() => new GameObject("model");

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.name == "model")
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        #region ValidateSelf

        [Test]
        public void UnitAsset_MissingId_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<UnitAsset>();
            asset.SetForEditor(string.Empty, "Villager", MakeModel());

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void UnitAsset_WrongIdPrefix_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<UnitAsset>();
            asset.SetForEditor("item.villager", "Villager", MakeModel());

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("unit.")));
        }

        [Test]
        public void UnitAsset_EmptyDisplayName_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<UnitAsset>();
            asset.SetForEditor("unit.villager", string.Empty, MakeModel());

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Display name")));
        }

        [Test]
        public void UnitAsset_MissingModel_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<UnitAsset>();
            asset.SetForEditor("unit.villager", "Villager", null);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Model")));
        }

        [Test]
        public void UnitAsset_Valid_HasNoProblems()
        {
            var asset = ScriptableObject.CreateInstance<UnitAsset>();
            asset.SetForEditor("unit.villager", "Villager", MakeModel());

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        [Test]
        public void ItemAsset_Valid_HasNoProblems()
        {
            var asset = ScriptableObject.CreateInstance<ItemAsset>();
            asset.SetForEditor("item.log", "Log", MakeModel(), 0, 5, 0, 0, 4);

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        [TestCase(0)]
        [TestCase(11)]
        public void ItemAsset_SizeOutsideOneToTen_IsReported(int size)
        {
            var asset = ScriptableObject.CreateInstance<ItemAsset>();
            asset.SetForEditor("item.log", "Log", MakeModel(), 0, 5, 0, 0, size);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Size")));
        }

        [Test]
        public void ItemAsset_NegativeSpoilTime_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<ItemAsset>();
            asset.SetForEditor("item.log", "Log", MakeModel(), 0, 5, 0, -1, 4);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Spoil time")));
        }

        [Test]
        public void ResourceAsset_FootprintWidthBelowOne_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<ResourceAsset>();
            asset.SetForEditor("resource.tree", "Tree", MakeModel(), 0, 1, "item.log", false, 5, 120, 60, 30);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Footprint width")));
        }

        [Test]
        public void ResourceAsset_FootprintDepthBelowOne_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<ResourceAsset>();
            asset.SetForEditor("resource.tree", "Tree", MakeModel(), 1, 0, "item.log", false, 5, 120, 60, 30);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Footprint depth")));
        }

        [Test]
        public void ResourceAsset_NegativeHarvestTime_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<ResourceAsset>();
            asset.SetForEditor("resource.tree", "Tree", MakeModel(), 1, 1, "item.log", false, 5, -1, 60, 30);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Harvest times")));
        }

        [Test]
        public void ResourceAsset_Valid_HasNoProblems()
        {
            var asset = ScriptableObject.CreateInstance<ResourceAsset>();
            asset.SetForEditor("resource.tree", "Tree", MakeModel(), 1, 1, "item.log", false, 5, 120, 60, 30);

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        [Test]
        public void BuildingAsset_FootprintBelowOne_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<BuildingAsset>();
            asset.SetForEditor("building.hut", "Hut", MakeModel(), 2, 0);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Footprint depth")));
        }

        [Test]
        public void BuildingAsset_Valid_HasNoProblems()
        {
            var asset = ScriptableObject.CreateInstance<BuildingAsset>();
            asset.SetForEditor("building.hut", "Hut", MakeModel(), 2, 2);

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        #endregion

        #region ContentAssetConverter

        private static ContentCatalog MakeCatalog(List<UnitAsset> units = null, List<ResourceAsset> resourceNodes = null, List<BuildingAsset> buildings = null, List<ItemAsset> items = null)
        {
            var catalog = ScriptableObject.CreateInstance<ContentCatalog>();
            catalog.SetContentsForEditor(
                units ?? new List<UnitAsset>(),
                resourceNodes ?? new List<ResourceAsset>(),
                buildings ?? new List<BuildingAsset>(),
                items ?? new List<ItemAsset>());
            return catalog;
        }

        [Test]
        public void BuildDefinitions_ConvertsEachTypeIntoItsRecord()
        {
            var unit = ScriptableObject.CreateInstance<UnitAsset>();
            unit.SetForEditor("unit.villager", "Villager", MakeModel());

            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            resource.SetForEditor("resource.tree", "Tree", MakeModel(), 1, 1, "item.log", false, 5, 120, 60, 30);

            var building = ScriptableObject.CreateInstance<BuildingAsset>();
            building.SetForEditor("building.hut", "Hut", MakeModel(), 2, 3);

            var item = ScriptableObject.CreateInstance<ItemAsset>();
            item.SetForEditor("item.log", "Log", MakeModel(), 0, 5, 0, 0, 4);

            ContentCatalog catalog = MakeCatalog(
                new List<UnitAsset> { unit },
                new List<ResourceAsset> { resource },
                new List<BuildingAsset> { building },
                new List<ItemAsset> { item });

            var problems = new List<ContentValidationProblem>();
            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(catalog, problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(4, definitions.Count);

            var unitRecord = (UnitContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.UnitTypeName).Definition;
            Assert.AreEqual("unit.villager", unitRecord.Id);
            Assert.AreEqual("Villager", unitRecord.DisplayName);

            var resourceRecord = (ResourceContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.ResourceTypeName).Definition;
            Assert.AreEqual("resource.tree", resourceRecord.Id);
            Assert.AreEqual(1, resourceRecord.FootprintWidth);
            Assert.AreEqual(1, resourceRecord.FootprintDepth);
            Assert.AreEqual("item.log", resourceRecord.YieldedItem.Id);
            Assert.IsFalse(resourceRecord.UnlimitedAmount);
            Assert.AreEqual(5, resourceRecord.AmountHeld);
            Assert.AreEqual(120, resourceRecord.HarvestTimeUnskilledMinutes);
            Assert.AreEqual(60, resourceRecord.HarvestTimeSkilledMinutes);
            Assert.AreEqual(30, resourceRecord.HarvestTimeMasterMinutes);

            var buildingRecord = (BuildingContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.BuildingTypeName).Definition;
            Assert.AreEqual("building.hut", buildingRecord.Id);
            Assert.AreEqual(2, buildingRecord.FootprintWidth);
            Assert.AreEqual(3, buildingRecord.FootprintDepth);

            var itemRecord = (ItemContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.ItemTypeName).Definition;
            Assert.AreEqual("item.log", itemRecord.Id);
            Assert.AreEqual("Log", itemRecord.DisplayName);
            Assert.AreEqual(5, itemRecord.CarryAmount);
            Assert.AreEqual(4, itemRecord.Size);
        }

        /// <summary>R5b: a missing model is reported, not silently skipped-and-logged - it goes into the same problem list as everything else ContentLoader finds, so it stops the game the same way.</summary>
        [Test]
        public void BuildDefinitions_ReportsAnAssetWithNoModel()
        {
            var unit = ScriptableObject.CreateInstance<UnitAsset>();
            unit.SetForEditor("unit.villager", "Villager", null);

            ContentCatalog catalog = MakeCatalog(units: new List<UnitAsset> { unit });

            var problems = new List<ContentValidationProblem>();
            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(catalog, problems);

            Assert.AreEqual(0, definitions.Count);
            Assert.IsTrue(problems.Any(p => p.ItemId == "unit.villager" && p.Field == "model"));
        }

        [Test]
        public void BuildDefinitions_SortsByIdInOrdinalOrder()
        {
            var a = ScriptableObject.CreateInstance<UnitAsset>();
            a.SetForEditor("unit.zebra", "Zebra", MakeModel());

            var b = ScriptableObject.CreateInstance<UnitAsset>();
            b.SetForEditor("unit.aardvark", "Aardvark", MakeModel());

            ContentCatalog catalog = MakeCatalog(units: new List<UnitAsset> { a, b });

            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(catalog, new List<ContentValidationProblem>());

            CollectionAssert.AreEqual(new[] { "unit.aardvark", "unit.zebra" }, definitions.ConvertAll(d => d.Definition.Id));
        }

        #endregion

        #region Through the real content pipeline

        private static ContentTypeRegistry CreateTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            return registry;
        }

        [Test]
        public void ContentLoader_UnitWithWrongPrefix_IsReported()
        {
            var loader = new ContentLoader(CreateTypes(), ErrorMode.Release);
            var assets = new[] { new ContentAssetDefinition(BuiltInContentTypes.UnitTypeName, new UnitContent { Id = "item.villager", DisplayName = "Villager" }, "content-assets", "unit.villager") };

            (_, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "item.villager" && p.Field == "id"));
        }

        [Test]
        public void ContentLoader_ResourceWithFootprintBelowOne_IsReported()
        {
            var loader = new ContentLoader(CreateTypes(), ErrorMode.Release);
            var assets = new[]
            {
                new ContentAssetDefinition(BuiltInContentTypes.ResourceTypeName, new ResourceContent { Id = "resource.tree", DisplayName = "Tree", FootprintWidth = 0, FootprintDepth = 1 }, "content-assets", "resource.tree")
            };

            (_, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "resource.tree" && p.Field == "footprintWidth"));
        }

        [Test]
        public void ContentLoader_ItemWithSizeOutsideOneToTen_IsReported()
        {
            var loader = new ContentLoader(CreateTypes(), ErrorMode.Release);
            var assets = new[]
            {
                new ContentAssetDefinition(BuiltInContentTypes.ItemTypeName, new ItemContent { Id = "item.log", DisplayName = "Log", Size = 11 }, "content-assets", "item.log")
            };

            (_, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "item.log" && p.Field == "size"));
        }

        /// <summary>R12 FINISH: "a yielded item that doesn't exist" - caught generically by ContentLoader's ContentRef resolution pass, not by a hand-written check.</summary>
        [Test]
        public void ContentLoader_ResourceYieldingAnItemThatDoesNotExist_IsReported()
        {
            var loader = new ContentLoader(CreateTypes(), ErrorMode.Release);
            var assets = new[]
            {
                new ContentAssetDefinition(
                    BuiltInContentTypes.ResourceTypeName,
                    new ResourceContent { Id = "resource.tree", DisplayName = "Tree", FootprintWidth = 1, FootprintDepth = 1, YieldedItem = new ContentRef<ItemContent>("item.does_not_exist") },
                    "content-assets",
                    "resource.tree")
            };

            (_, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "resource.tree" && p.Message.Contains("item.does_not_exist")));
        }

        [Test]
        public void ContentLoader_DuplicateUnitId_IsReported()
        {
            var loader = new ContentLoader(CreateTypes(), ErrorMode.Release);
            var assets = new[]
            {
                new ContentAssetDefinition(BuiltInContentTypes.UnitTypeName, new UnitContent { Id = "unit.villager", DisplayName = "Villager" }, "content-assets", "a"),
                new ContentAssetDefinition(BuiltInContentTypes.UnitTypeName, new UnitContent { Id = "unit.villager", DisplayName = "Villager" }, "content-assets", "b")
            };

            (_, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "unit.villager" && p.Field == "id"));
        }

        /// <summary>R5b, FINISH: a missing model is reported alongside an unrelated content problem in the same run, and Development mode's single exception carries both.</summary>
        [Test]
        public void MissingModelProblem_FlowsThroughContentLoader_AlongsideOtherProblems()
        {
            var unit = ScriptableObject.CreateInstance<UnitAsset>();
            unit.SetForEditor("unit.villager", "Villager", null);

            ContentCatalog catalog = MakeCatalog(units: new List<UnitAsset> { unit });

            var contentAssetProblems = new List<ContentValidationProblem>();
            ContentAssetConverter.BuildDefinitions(catalog, contentAssetProblems);

            var otherAssets = new[]
            {
                new ContentAssetDefinition(BuiltInContentTypes.ResourceTypeName, new ResourceContent { Id = "resource.tree", DisplayName = "Tree", FootprintWidth = 0, FootprintDepth = 1 }, "content-assets", "resource.tree")
            };

            var loader = new ContentLoader(CreateTypes(), ErrorMode.Development);

            ContentValidationException ex = Assert.Throws<ContentValidationException>(() =>
                loader.Load(null, assetDefinitions: otherAssets, extraProblems: contentAssetProblems));

            Assert.IsTrue(ex.Report.Problems.Any(p => p.ItemId == "unit.villager" && p.Field == "model"));
            Assert.IsTrue(ex.Report.Problems.Any(p => p.ItemId == "resource.tree" && p.Field == "footprintWidth"));
        }

        #endregion
    }
}
