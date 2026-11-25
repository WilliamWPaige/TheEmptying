using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Resource", fileName = "resource")]
    public sealed class ResourceAsset : ContentAssetBase
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject model;
        [SerializeField] private int footprintWidth = 1;
        [SerializeField] private int footprintDepth = 1;
        [SerializeField] private string yieldedItemId;
        [SerializeField] private bool unlimitedAmount;
        [SerializeField] private int amountHeld;
        [SerializeField] private double harvestTimeUnskilledMinutes;
        [SerializeField] private double harvestTimeSkilledMinutes;
        [SerializeField] private double harvestTimeMasterMinutes;
        [SerializeField] private int maxWorkers = 1;

        /// <summary>R15: "gather nearby" fields - standing on a free ground cell within radius of this resource (never on the resource itself) gathers gatherItemId, instead of harvesting the resource directly. Empty gatherItemId means this resource has no gather-nearby behavior.</summary>
        [SerializeField] private string gatherItemId;

        [SerializeField] private double gatherRadiusMeters;
        [SerializeField] private double gatherTimeUnskilledMinutes;
        [SerializeField] private double gatherTimeSkilledMinutes;
        [SerializeField] private double gatherTimeMasterMinutes;

        /// <summary>R15: how many people may gather near this resource at once - a resource at its limit is treated as unavailable, the same crowding rule MaxWorkers applies to direct harvesting.</summary>
        [SerializeField] private int gatherMaxConcurrent = 1;

        /// <summary>R15: an optional extra item (e.g. flint while gathering rock) also given by chance (gatherBonusChance) each time gatherItemId is gathered; empty means no bonus.</summary>
        [SerializeField] private string gatherBonusItemId;

        [SerializeField] private double gatherBonusChance;

        public override string Id => id;
        public override string IdPrefix => "resource.";
        public override string ContentTypeName => BuiltInContentTypes.ResourceTypeName;
        public override string DisplayName => displayName;
        public override GameObject Model => model;
        public int FootprintWidth => footprintWidth;
        public int FootprintDepth => footprintDepth;
        public string YieldedItemId => yieldedItemId;
        public bool UnlimitedAmount => unlimitedAmount;
        public int AmountHeld => amountHeld;
        public double HarvestTimeUnskilledMinutes => harvestTimeUnskilledMinutes;
        public double HarvestTimeSkilledMinutes => harvestTimeSkilledMinutes;
        public double HarvestTimeMasterMinutes => harvestTimeMasterMinutes;
        public int MaxWorkers => maxWorkers;
        public string GatherItemId => gatherItemId;
        public double GatherRadiusMeters => gatherRadiusMeters;
        public double GatherTimeUnskilledMinutes => gatherTimeUnskilledMinutes;
        public double GatherTimeSkilledMinutes => gatherTimeSkilledMinutes;
        public double GatherTimeMasterMinutes => gatherTimeMasterMinutes;
        public int GatherMaxConcurrent => gatherMaxConcurrent;
        public string GatherBonusItemId => gatherBonusItemId;
        public double GatherBonusChance => gatherBonusChance;

        public override IEnumerable<string> ValidateSelf()
        {
            foreach (string problem in base.ValidateSelf())
            {
                yield return problem;
            }

            if (footprintWidth < 1)
            {
                yield return "Footprint width must be at least 1.";
            }

            if (footprintDepth < 1)
            {
                yield return "Footprint depth must be at least 1.";
            }

            if (harvestTimeUnskilledMinutes < 0 || harvestTimeSkilledMinutes < 0 || harvestTimeMasterMinutes < 0)
            {
                yield return "Harvest times must not be negative.";
            }

            if (maxWorkers < 1)
            {
                yield return "Max workers must be at least 1.";
            }

            if (!string.IsNullOrEmpty(gatherItemId))
            {
                if (gatherRadiusMeters <= 0)
                {
                    yield return "Gather radius must be greater than 0 when a gather item is set.";
                }

                if (gatherTimeUnskilledMinutes < 0 || gatherTimeSkilledMinutes < 0 || gatherTimeMasterMinutes < 0)
                {
                    yield return "Gather times must not be negative.";
                }

                if (gatherMaxConcurrent < 1)
                {
                    yield return "Gather max concurrent must be at least 1.";
                }
            }

            if (gatherBonusChance < 0 || gatherBonusChance > 1)
            {
                yield return "Gather bonus chance must be between 0 and 1.";
            }
        }

#if UNITY_EDITOR
        public void SetForEditor(
            string id,
            string displayName,
            GameObject model,
            int footprintWidth,
            int footprintDepth,
            string yieldedItemId,
            bool unlimitedAmount,
            int amountHeld,
            double harvestTimeUnskilledMinutes,
            double harvestTimeSkilledMinutes,
            double harvestTimeMasterMinutes,
            int maxWorkers)
        {
            this.id = id;
            this.displayName = displayName;
            this.model = model;
            this.footprintWidth = footprintWidth;
            this.footprintDepth = footprintDepth;
            this.yieldedItemId = yieldedItemId;
            this.unlimitedAmount = unlimitedAmount;
            this.amountHeld = amountHeld;
            this.harvestTimeUnskilledMinutes = harvestTimeUnskilledMinutes;
            this.harvestTimeSkilledMinutes = harvestTimeSkilledMinutes;
            this.harvestTimeMasterMinutes = harvestTimeMasterMinutes;
            this.maxWorkers = maxWorkers;
        }
#endif
    }
}
