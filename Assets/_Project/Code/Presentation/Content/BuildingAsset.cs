using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Building", fileName = "building")]
    public sealed class BuildingAsset : ContentAssetBase
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject model;
        [SerializeField] private int footprintWidth = 1;
        [SerializeField] private int footprintDepth = 1;

        public override string Id => id;
        public override string IdPrefix => "building.";
        public override string ContentTypeName => BuiltInContentTypes.BuildingTypeName;
        public override string DisplayName => displayName;
        public override GameObject Model => model;
        public int FootprintWidth => footprintWidth;
        public int FootprintDepth => footprintDepth;

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
        }

#if UNITY_EDITOR
        public void SetForEditor(string id, string displayName, GameObject model, int footprintWidth, int footprintDepth)
        {
            this.id = id;
            this.displayName = displayName;
            this.model = model;
            this.footprintWidth = footprintWidth;
            this.footprintDepth = footprintDepth;
        }
#endif
    }
}
