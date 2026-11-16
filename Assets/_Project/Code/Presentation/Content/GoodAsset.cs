using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Content/Good", fileName = "Good")]
    public sealed class GoodAsset : ContentAssetBase
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private GoodCategory _category;
        [SerializeField, Tooltip("Price in shil (fractions allowed, e.g. 2.5). Converted to whole hundredths of a shil.")] private float _basePriceShil;
        [SerializeField, TextArea] private string _description;

        public override string Id => _id;
        public override string IdPrefix => "good.";
        public override string ContentTypeName => BuiltInContentTypes.GoodTypeName;

        public string DisplayName => _displayName;
        public GoodCategory Category => _category;
        public float BasePriceShil => _basePriceShil;
        public string Description => _description;

        public override IEnumerable<string> ValidateSelf()
        {
            foreach (string problem in base.ValidateSelf())
            {
                yield return problem;
            }

            if (_basePriceShil < 0f)
            {
                yield return "Base price must not be negative.";
            }
        }

#if UNITY_EDITOR
        public void SetForEditor(string id, string displayName, GoodCategory category, float basePriceShil, string description)
        {
            _id = id;
            _displayName = displayName;
            _category = category;
            _basePriceShil = basePriceShil;
            _description = description;
        }
#endif
    }
}
