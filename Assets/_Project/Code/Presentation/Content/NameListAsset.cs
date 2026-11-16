using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Content/Name List", fileName = "NameList")]
    public sealed class NameListAsset : ContentAssetBase
    {
        [SerializeField] private string _id;
        [SerializeField] private NameListKind _kind;
        [SerializeField] private List<string> _names = new List<string>();
        [SerializeField, TextArea] private string _description;

        public override string Id => _id;
        public override string IdPrefix => "name.";
        public override string ContentTypeName => BuiltInContentTypes.NameListTypeName;

        public NameListKind Kind => _kind;
        public IReadOnlyList<string> Names => _names;
        public string Description => _description;

        public override IEnumerable<string> ValidateSelf()
        {
            foreach (string problem in base.ValidateSelf())
            {
                yield return problem;
            }

            if (_names.Count == 0)
            {
                yield return "Names must not be empty.";
            }
        }

#if UNITY_EDITOR
        public void SetForEditor(string id, NameListKind kind, List<string> names, string description)
        {
            _id = id;
            _kind = kind;
            _names = names ?? new List<string>();
            _description = description;
        }
#endif
    }
}
