using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Content/Zone", fileName = "Zone")]
    public sealed class ZoneAsset : ContentAssetBase
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private bool _isResidential;
        [SerializeField, Tooltip("Only meaningful when this zone is residential.")] private SocialClass _classTier;
        [SerializeField, TextArea] private string _description;

        public override string Id => _id;
        public override string IdPrefix => "zone.";
        public override string ContentTypeName => BuiltInContentTypes.ZoneTypeName;

        public string DisplayName => _displayName;
        public SocialClass? ClassTier => _isResidential ? _classTier : (SocialClass?)null;
        public string Description => _description;

#if UNITY_EDITOR
        public void SetForEditor(string id, string displayName, SocialClass? classTier, string description)
        {
            _id = id;
            _displayName = displayName;
            _isResidential = classTier.HasValue;
            _classTier = classTier ?? default;
            _description = description;
        }
#endif
    }
}
