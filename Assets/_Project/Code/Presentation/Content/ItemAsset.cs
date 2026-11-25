using System.Collections.Generic;
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
        [SerializeField] private double foodValue;
        [SerializeField] private int carryAmount = 1;

        /// <summary>R15: tenths of a copper (so a 0.2 copper placeholder value is the exact integer 2) - whole-copper amounts stay exact too (1 copper is 10), and payment math never touches a fraction that isn't a multiple of 0.1 copper.</summary>
        [SerializeField] private int baseValueTenthCopper;

        [SerializeField] private double spoilTimeMinutes;
        [SerializeField] private int size = 1;

        public override string Id => id;
        public override string IdPrefix => "item.";
        public override string ContentTypeName => BuiltInContentTypes.ItemTypeName;
        public override string DisplayName => displayName;
        public override GameObject Model => model;
        public double FoodValue => foodValue;
        public int CarryAmount => carryAmount;
        public int BaseValueTenthCopper => baseValueTenthCopper;
        public double SpoilTimeMinutes => spoilTimeMinutes;
        public int Size => size;

        public override IEnumerable<string> ValidateSelf()
        {
            foreach (string problem in base.ValidateSelf())
            {
                yield return problem;
            }

            if (size < 1 || size > 10)
            {
                yield return "Size must be between 1 and 10.";
            }

            if (spoilTimeMinutes < 0)
            {
                yield return "Spoil time must not be negative.";
            }
        }

#if UNITY_EDITOR
        public void SetForEditor(string id, string displayName, GameObject model, double foodValue, int carryAmount, int baseValueTenthCopper, double spoilTimeMinutes, int size)
        {
            this.id = id;
            this.displayName = displayName;
            this.model = model;
            this.foodValue = foodValue;
            this.carryAmount = carryAmount;
            this.baseValueTenthCopper = baseValueTenthCopper;
            this.spoilTimeMinutes = spoilTimeMinutes;
            this.size = size;
        }
#endif
    }
}
