using System;
using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>R20: one stack model - three stage slots (stage 1 from any amount, stage 2 from a third of capacity, stage 3 from two thirds). Any stage left empty falls back to the placeholder procedural brown boxes until the real model exists.</summary>
    [Serializable]
    public sealed class StackModelDefinition
    {
        [SerializeField] private GameObject stage1Model;
        [SerializeField] private GameObject stage2Model;
        [SerializeField] private GameObject stage3Model;

        public GameObject Stage1Model => stage1Model;
        public GameObject Stage2Model => stage2Model;
        public GameObject Stage3Model => stage3Model;
    }

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

        /// <summary>R22: how much of a stockpile spot one pile of this item covers.</summary>
        [SerializeField] private PileShape pileShape = PileShape.Quarter;

        /// <summary>R20: a stockpile stack of this item picks one of these at random (and keeps it until it empties). Until William's own models exist, every slot here is left empty, so every stack renders as the placeholder brown boxes. R22: each model now represents this item's own PileShape, not necessarily a full spot.</summary>
        [SerializeField] private List<StackModelDefinition> stackModels = new List<StackModelDefinition>();

        /// <summary>R20: shown above each of this item's stacks; null falls back to the placeholder colored-badge-with-a-letter.</summary>
        [SerializeField] private Sprite icon;

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
        public PileShape PileShape => pileShape;
        public IReadOnlyList<StackModelDefinition> StackModels => stackModels;
        public Sprite Icon => icon;

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
