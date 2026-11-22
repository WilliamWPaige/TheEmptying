using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Item", fileName = "item")]
    public sealed class ItemAsset : ContentAssetBase
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject model;

        public override string Id => id;
        public override string IdPrefix => "item.";
        public override string ContentTypeName => BuiltInContentTypes.ItemTypeName;
        public override string DisplayName => displayName;
        public override GameObject Model => model;

#if UNITY_EDITOR
        public void SetForEditor(string id, string displayName, GameObject model)
        {
            this.id = id;
            this.displayName = displayName;
            this.model = model;
        }
#endif
    }
}
