using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Modules;
using TheEmptying.Presentation.Content;
using UnityEngine;
using UnityEngine.TestTools;

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
            asset.SetForEditor("item.log", "Log", MakeModel());

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        [Test]
        public void ResourceAsset_FootprintWidthBelowOne_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<ResourceAsset>();
            asset.SetForEditor("resource.tree", "Tree", MakeModel(), 0, 1);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Footprint width")));
        }

        [Test]
        public void ResourceAsset_FootprintDepthBelowOne_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<ResourceAsset>();
            asset.SetForEditor("resource.tree", "Tree", MakeModel(), 1, 0);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("Footprint depth")));
        }

        [Test]
        public void ResourceAsset_Valid_HasNoProblems()
        {
            var asset = ScriptableObject.CreateInstance<ResourceAsset>();
            asset.SetForEditor("resource.tree", "Tree", MakeModel(), 1, 1);

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
            resource.SetForEditor("resource.tree", "Tree", MakeModel(), 1, 1);

            var building = ScriptableObject.CreateInstance<BuildingAsset>();
            building.SetForEditor("building.hut", "Hut", MakeModel(), 2, 3);

            var item = ScriptableObject.CreateInstance<ItemAsset>();
            item.SetForEditor("item.log", "Log", MakeModel());

            ContentCatalog catalog = MakeCatalog(
                new List<UnitAsset> { unit },
                new List<ResourceAsset> { resource },
                new List<BuildingAsset> { building },
                new List<ItemAsset> { item });

            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(catalog);

            Assert.AreEqual(4, definitions.Count);

            var unitRecord = (UnitContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.UnitTypeName).Definition;
            Assert.AreEqual("unit.villager", unitRecord.Id);
            Assert.AreEqual("Villager", unitRecord.DisplayName);

            var resourceRecord = (ResourceContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.ResourceTypeName).Definition;
            Assert.AreEqual("resource.tree", resourceRecord.Id);
            Assert.AreEqual(1, resourceRecord.FootprintWidth);
            Assert.AreEqual(1, resourceRecord.FootprintDepth);

            var buildingRecord = (BuildingContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.BuildingTypeName).Definition;
            Assert.AreEqual("building.hut", buildingRecord.Id);
            Assert.AreEqual(2, buildingRecord.FootprintWidth);
            Assert.AreEqual(3, buildingRecord.FootprintDepth);

            var itemRecord = (ItemContent)definitions.Single(d => d.TypeName == BuiltInContentTypes.ItemTypeName).Definition;
            Assert.AreEqual("item.log", itemRecord.Id);
            Assert.AreEqual("Log", itemRecord.DisplayName);
        }

        [Test]
        public void BuildDefinitions_SkipsAndLogsAnAssetWithNoModel()
        {
            var unit = ScriptableObject.CreateInstance<UnitAsset>();
            unit.SetForEditor("unit.villager", "Villager", null);

            ContentCatalog catalog = MakeCatalog(units: new List<UnitAsset> { unit });

            LogAssert.Expect(LogType.Error, new Regex("has no Model"));
            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(catalog);

            Assert.AreEqual(0, definitions.Count);
        }

        [Test]
        public void BuildDefinitions_SortsByIdInOrdinalOrder()
        {
            var a = ScriptableObject.CreateInstance<UnitAsset>();
            a.SetForEditor("unit.zebra", "Zebra", MakeModel());

            var b = ScriptableObject.CreateInstance<UnitAsset>();
            b.SetForEditor("unit.aardvark", "Aardvark", MakeModel());

            ContentCatalog catalog = MakeCatalog(units: new List<UnitAsset> { a, b });

            List<ContentAssetDefinition> definitions = ContentAssetConverter.BuildDefinitions(catalog);

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

        #endregion
    }
}
