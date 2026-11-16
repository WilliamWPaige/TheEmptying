using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Content;
using TheEmptying.Modules.People;
using TheEmptying.Presentation.Content;
using UnityEngine;

namespace TheEmptying.Tests.EditMode.Content
{
    /// <summary>Each content ScriptableObject's own ValidateSelf(), the same checks the Inspector and "Validate All Content" show.</summary>
    public class ContentAssetSelfValidationTests
    {
        [Test]
        public void GoodAsset_MissingId_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<GoodAsset>();
            asset.SetForEditor(string.Empty, "Log", GoodCategory.Raw, 5, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void GoodAsset_WrongIdPrefix_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<GoodAsset>();
            asset.SetForEditor("resource.log", "Log", GoodCategory.Raw, 5, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any(p => p.Contains("good.")));
        }

        [Test]
        public void GoodAsset_NegativePrice_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<GoodAsset>();
            asset.SetForEditor("good.log", "Log", GoodCategory.Raw, -1, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void GoodAsset_Valid_HasNoProblems()
        {
            var asset = ScriptableObject.CreateInstance<GoodAsset>();
            asset.SetForEditor("good.log", "Log", GoodCategory.Raw, 5, string.Empty);

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        [Test]
        public void NeedAsset_MeterWithZeroHoursToEmpty_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<NeedAsset>();
            asset.SetForEditor("need.hunger", "Hunger", NeedKind.Meter, 0f, SocialClass.Lower, false, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void ZoneAsset_Valid_HasNoProblems()
        {
            var asset = ScriptableObject.CreateInstance<ZoneAsset>();
            asset.SetForEditor("zone.farm", "Farm", null, string.Empty);

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        [Test]
        public void BuildingTypeAsset_ConstructionCostWithNoGoodAssigned_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<BuildingTypeAsset>();
            asset.SetForEditor("building.cottage", "Cottage", 2, 2,
                new System.Collections.Generic.List<GoodAmountAsset> { new GoodAmountAsset(null, 5) },
                0, 0, 0, true, false, new System.Collections.Generic.List<ZoneAsset>(), 1, SocialClass.Lower, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void BuildingTypeAsset_NegativeConstructionCostAmount_IsReported()
        {
            var good = ScriptableObject.CreateInstance<GoodAsset>();
            good.SetForEditor("good.plank", "Plank", GoodCategory.Processed, 4, string.Empty);

            var asset = ScriptableObject.CreateInstance<BuildingTypeAsset>();
            asset.SetForEditor("building.cottage", "Cottage", 2, 2,
                new System.Collections.Generic.List<GoodAmountAsset> { new GoodAmountAsset(good, -5) },
                0, 0, 0, true, false, new System.Collections.Generic.List<ZoneAsset>(), 1, SocialClass.Lower, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void BuildingTypeAsset_NonGovernmentWithNoZones_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<BuildingTypeAsset>();
            asset.SetForEditor("building.cottage", "Cottage", 2, 2,
                new System.Collections.Generic.List<GoodAmountAsset>(),
                0, 0, 0, true, false, new System.Collections.Generic.List<ZoneAsset>(), 1, SocialClass.Lower, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void BuildingTypeAsset_GovernmentWithNoZones_IsNotReported()
        {
            var asset = ScriptableObject.CreateInstance<BuildingTypeAsset>();
            asset.SetForEditor("building.crown_storehouse", "Crown Storehouse", 4, 4,
                new System.Collections.Generic.List<GoodAmountAsset>(),
                0, 0, 0, false, true, new System.Collections.Generic.List<ZoneAsset>(), 0, null, string.Empty);

            Assert.IsFalse(asset.ValidateSelf().Any());
        }

        [Test]
        public void RecipeAsset_MissingWorkplace_IsReported()
        {
            var asset = ScriptableObject.CreateInstance<RecipeAsset>();
            asset.SetForEditor("recipe.woodcutting", null,
                new System.Collections.Generic.List<GoodAmountAsset>(), new System.Collections.Generic.List<GoodAmountAsset>(),
                1f, WorkerTier.Laborer, Skill.Logging, false, new System.Collections.Generic.List<TheEmptying.Core.Timekeeping.Season>(), 0, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }

        [Test]
        public void RecipeAsset_ZeroDuration_IsReported()
        {
            var workplace = ScriptableObject.CreateInstance<BuildingTypeAsset>();
            workplace.SetForEditor("building.woodcutters_camp", "Woodcutter's Camp", 3, 3,
                new System.Collections.Generic.List<GoodAmountAsset>(), 2, 0, 0, false, false,
                new System.Collections.Generic.List<ZoneAsset>(), 0, null, string.Empty);

            var asset = ScriptableObject.CreateInstance<RecipeAsset>();
            asset.SetForEditor("recipe.woodcutting", workplace,
                new System.Collections.Generic.List<GoodAmountAsset>(), new System.Collections.Generic.List<GoodAmountAsset>(),
                0f, WorkerTier.Laborer, Skill.Logging, false, new System.Collections.Generic.List<TheEmptying.Core.Timekeeping.Season>(), 0, string.Empty);

            Assert.IsTrue(asset.ValidateSelf().Any());
        }
    }
}
