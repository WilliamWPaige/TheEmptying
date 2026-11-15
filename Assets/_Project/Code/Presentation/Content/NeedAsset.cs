using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Content/Need", fileName = "Need")]
    public sealed class NeedAsset : ContentAssetBase
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private NeedKind _kind;

        [SerializeField, Tooltip("Hours until this meter need runs out, starting full. Ignored for a state need.")]
        private float _hoursToEmpty;

        [SerializeField, Tooltip("The lowest class that has this need: Lower means all classes, Middle means middle and up, Upper means upper only.")]
        private SocialClass _minimumClass;

        [SerializeField] private bool _believersOnly;
        [SerializeField, TextArea] private string _description;

        public override string Id => _id;
        public override string IdPrefix => "need.";
        public override string ContentTypeName => BuiltInContentTypes.NeedTypeName;

        public string DisplayName => _displayName;
        public NeedKind Kind => _kind;
        public float HoursToEmpty => _hoursToEmpty;
        public SocialClass MinimumClass => _minimumClass;
        public bool BelieversOnly => _believersOnly;
        public string Description => _description;

        public override IEnumerable<string> ValidateSelf()
        {
            foreach (string problem in base.ValidateSelf())
            {
                yield return problem;
            }

            if (_kind == NeedKind.Meter && _hoursToEmpty <= 0f)
            {
                yield return "Hours to empty must be greater than 0 for a meter need.";
            }
        }

#if UNITY_EDITOR
        public void SetForEditor(string id, string displayName, NeedKind kind, float hoursToEmpty, SocialClass minimumClass, bool believersOnly, string description)
        {
            _id = id;
            _displayName = displayName;
            _kind = kind;
            _hoursToEmpty = hoursToEmpty;
            _minimumClass = minimumClass;
            _believersOnly = believersOnly;
            _description = description;
        }
#endif
    }
}
