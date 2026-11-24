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
