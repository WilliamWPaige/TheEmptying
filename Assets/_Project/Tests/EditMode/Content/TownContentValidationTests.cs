using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Timekeeping;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>
    /// Validates the gray-box town's content records (Good, Need, Zone,
    /// BuildingType, Recipe), built directly in code - no ScriptableObjects
    /// anywhere in this file - exactly as a simulation-side test should.
    /// </summary>
    public class TownContentValidationTests
    {
        private static ContentTypeRegistry CreateTypes()
        {
            var registry = new ContentTypeRegistry();
            BuiltInContentTypes.RegisterAll(registry);
            return registry;
        }

        private static ContentLoadReport LoadAssets(IEnumerable<ContentAssetDefinition> assets, ErrorMode errorMode = ErrorMode.Release)
        {
            var loader = new ContentLoader(CreateTypes(), errorMode);
            (_, ContentLoadReport report) = loader.Load(null, assetDefinitions: assets);
            return report;
        }

        private static ContentAssetDefinition Asset(string typeName, ContentDefinition definition, string fileName) =>
            new ContentAssetDefinition(typeName, definition, "content-assets", fileName);

        [Test]
        public void Good_WithWrongIdPrefix_IsReported()
        {
            var good = new Good("resource.log", "Log", GoodCategory.Raw, Money.FromShil(5));
            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.GoodTypeName, good, "Log") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "resource.log" && p.Field == "id"));
        }

        [Test]
        public void Good_WithNegativePrice_IsReported()
        {
            var good = new Good("good.log", "Log", GoodCategory.Raw, Money.FromHundredthsOfShil(-1));
            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.GoodTypeName, good, "Log") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "good.log" && p.Field == "basePrice"));
        }

        [Test]
        public void Need_MeterWithZeroHoursToEmpty_IsReported()
        {
            var need = new Need("need.hunger", "Hunger", NeedKind.Meter, 0f, SocialClass.Lower, false);
            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.NeedTypeName, need, "Hunger") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "need.hunger" && p.Field == "hoursToEmpty"));
        }

        [Test]
        public void Need_StateWithZeroHoursToEmpty_IsNotReported()
        {
            var need = new Need("need.shelter", "Shelter", NeedKind.State, 0f, SocialClass.Lower, false);
            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.NeedTypeName, need, "Shelter") });

            Assert.IsFalse(report.HasErrors);
        }

        [Test]
        public void Zone_WithWrongIdPrefix_IsReported()
        {
            var zone = new Zone("area.farm", "Farm", null);
            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.ZoneTypeName, zone, "Farm") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "area.farm" && p.Field == "id"));
        }

        [Test]
        public void BuildingType_WithNegativeConstructionCost_IsReported()
        {
            var good = new Good("good.plank", "Plank", GoodCategory.Processed, Money.FromShil(4));
            var buildingType = new BuildingType(
                "building.cottage", "Cottage", 2, 2,
                new List<GoodAmount> { new GoodAmount(new ContentRef<Good>("good.plank"), -5) },
                0, 0, 0, true, true, new List<ContentRef<Zone>>(), 1, SocialClass.Lower);

            ContentLoadReport report = LoadAssets(new[]
            {
                Asset(BuiltInContentTypes.GoodTypeName, good, "Plank"),
                Asset(BuiltInContentTypes.BuildingTypeTypeName, buildingType, "Cottage")
            });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "building.cottage" && p.Field == "constructionCost"));
        }

        [Test]
        public void BuildingType_ReferencingAMissingZone_IsReported()
        {
            var buildingType = new BuildingType(
                "building.cottage", "Cottage", 2, 2, new List<GoodAmount>(),
                0, 0, 0, true, false, new List<ContentRef<Zone>> { new ContentRef<Zone>("zone.does_not_exist") }, 1, SocialClass.Lower);

            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.BuildingTypeTypeName, buildingType, "Cottage") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "building.cottage" && p.Field == "allowedZones" && p.Message.Contains("zone.does_not_exist")));
        }

        [Test]
        public void BuildingType_NonGovernmentWithNoZones_IsReported()
        {
            var buildingType = new BuildingType(
                "building.cottage", "Cottage", 2, 2, new List<GoodAmount>(),
                0, 0, 0, true, false, new List<ContentRef<Zone>>(), 1, SocialClass.Lower);

            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.BuildingTypeTypeName, buildingType, "Cottage") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "building.cottage" && p.Field == "allowedZones" && p.Message.Contains("government")));
        }

        [Test]
        public void BuildingType_GovernmentWithNoZones_IsNotReported()
        {
            var buildingType = new BuildingType(
                "building.crown_storehouse", "Crown Storehouse", 4, 4, new List<GoodAmount>(),
                0, 0, 0, false, true, new List<ContentRef<Zone>>(), 0, null);

            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.BuildingTypeTypeName, buildingType, "CrownStorehouse") }, ErrorMode.Development);

            Assert.IsFalse(report.HasErrors);
        }

        [Test]
        public void Recipe_WithZeroDuration_IsReported()
        {
            var recipe = new Recipe(
                "recipe.woodcutting", new ContentRef<BuildingType>("building.woodcutters_camp"),
                new List<GoodAmount>(), new List<GoodAmount>(), 0f, WorkerTier.Laborer, false, new List<Season>(), 0);

            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.RecipeTypeName, recipe, "Woodcutting") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "recipe.woodcutting" && p.Field == "durationHours"));
        }

        [Test]
        public void Recipe_ReferencingAMissingWorkplace_IsReported()
        {
            var recipe = new Recipe(
                "recipe.woodcutting", new ContentRef<BuildingType>("building.does_not_exist"),
                new List<GoodAmount>(), new List<GoodAmount>(), 1f, WorkerTier.Laborer, false, new List<Season>(), 0);

            ContentLoadReport report = LoadAssets(new[] { Asset(BuiltInContentTypes.RecipeTypeName, recipe, "Woodcutting") });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "recipe.woodcutting" && p.Field == "Workplace"));
        }

        [Test]
        public void Recipe_WithNegativeInputAmount_IsReported()
        {
            var good = new Good("good.wheat", "Wheat", GoodCategory.Raw, Money.FromShil(1));
            var recipe = new Recipe(
                "recipe.milling_flour", new ContentRef<BuildingType>("building.mill"),
                new List<GoodAmount> { new GoodAmount(new ContentRef<Good>("good.wheat"), -4) },
                new List<GoodAmount>(), 0.5f, WorkerTier.Skilled, false, new List<Season>(), 0);

            ContentLoadReport report = LoadAssets(new[]
            {
                Asset(BuiltInContentTypes.GoodTypeName, good, "Wheat"),
                Asset(BuiltInContentTypes.RecipeTypeName, recipe, "MillingFlour")
            });

            Assert.IsTrue(report.Problems.Any(p => p.ItemId == "recipe.milling_flour" && p.Field == "inputs"));
        }

        [Test]
        public void ValidTownContent_LoadsWithNoProblems()
        {
            var plank = new Good("good.plank", "Plank", GoodCategory.Processed, Money.FromShil(4));
            var residentialLow = new Zone("zone.residential_low", "Residential Low", SocialClass.Lower);
            var woodcuttersCamp = new BuildingType(
                "building.woodcutters_camp", "Woodcutter's Camp", 3, 3, new List<GoodAmount>(),
                2, 0, 0, false, false, new List<ContentRef<Zone>> { new ContentRef<Zone>("zone.residential_low") }, 0, null);
            var woodcutting = new Recipe(
                "recipe.woodcutting", new ContentRef<BuildingType>("building.woodcutters_camp"),
                new List<GoodAmount>(), new List<GoodAmount> { new GoodAmount(new ContentRef<Good>("good.plank"), 1) },
                1f, WorkerTier.Laborer, false, new List<Season>(), 0);

            ContentLoadReport report = LoadAssets(new[]
            {
                Asset(BuiltInContentTypes.GoodTypeName, plank, "Plank"),
                Asset(BuiltInContentTypes.ZoneTypeName, residentialLow, "ResidentialLow"),
                Asset(BuiltInContentTypes.BuildingTypeTypeName, woodcuttersCamp, "WoodcuttersCamp"),
                Asset(BuiltInContentTypes.RecipeTypeName, woodcutting, "Woodcutting")
            }, ErrorMode.Development);

            Assert.IsFalse(report.HasErrors);
        }
    }
}
