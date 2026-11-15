using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Timekeeping;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Content/Recipe", fileName = "Recipe")]
    public sealed class RecipeAsset : ContentAssetBase
    {
        [SerializeField] private string _id;
        [SerializeField] private BuildingTypeAsset _workplace;
        [SerializeField] private List<GoodAmountAsset> _inputs = new List<GoodAmountAsset>();
        [SerializeField] private List<GoodAmountAsset> _outputs = new List<GoodAmountAsset>();
        [SerializeField] private float _durationHours;
        [SerializeField] private WorkerTier _workerTier;
        [SerializeField] private bool _isHardLabor;
        [SerializeField] private List<Season> _seasonsAllowed = new List<Season>();

        [SerializeField, Tooltip("Months until a crop recipe's output is ready. 0 for a recipe that isn't a crop.")]
        private int _growthTimeMonths;

        [SerializeField, TextArea] private string _description;

        public override string Id => _id;
        public override string IdPrefix => "recipe.";
        public override string ContentTypeName => BuiltInContentTypes.RecipeTypeName;

        public BuildingTypeAsset Workplace => _workplace;
        public IReadOnlyList<GoodAmountAsset> Inputs => _inputs;
        public IReadOnlyList<GoodAmountAsset> Outputs => _outputs;
        public float DurationHours => _durationHours;
        public WorkerTier WorkerTier => _workerTier;
        public bool IsHardLabor => _isHardLabor;
        public IReadOnlyList<Season> SeasonsAllowed => _seasonsAllowed;
        public int GrowthTimeMonths => _growthTimeMonths;
        public string Description => _description;

        public override IEnumerable<string> ValidateSelf()
        {
            foreach (string problem in base.ValidateSelf())
            {
                yield return problem;
            }

            if (_workplace == null)
            {
                yield return "Workplace building type is missing.";
            }

            if (_durationHours <= 0f)
            {
                yield return "Duration must be greater than 0.";
            }

            foreach (GoodAmountAsset input in _inputs)
            {
                if (input.Good == null)
                {
                    yield return "Inputs has an entry with no good assigned.";
                }
                else if (input.Amount < 0)
                {
                    yield return $"Input amount for '{input.Good.Id}' must not be negative.";
                }
            }

            foreach (GoodAmountAsset output in _outputs)
            {
                if (output.Good == null)
                {
                    yield return "Outputs has an entry with no good assigned.";
                }
                else if (output.Amount < 0)
                {
                    yield return $"Output amount for '{output.Good.Id}' must not be negative.";
                }
            }
        }

#if UNITY_EDITOR
        public void SetForEditor(
            string id,
            BuildingTypeAsset workplace,
            List<GoodAmountAsset> inputs,
            List<GoodAmountAsset> outputs,
            float durationHours,
            WorkerTier workerTier,
            bool isHardLabor,
            List<Season> seasonsAllowed,
            int growthTimeMonths,
            string description)
        {
            _id = id;
            _workplace = workplace;
            _inputs = inputs ?? new List<GoodAmountAsset>();
            _outputs = outputs ?? new List<GoodAmountAsset>();
            _durationHours = durationHours;
            _workerTier = workerTier;
            _isHardLabor = isHardLabor;
            _seasonsAllowed = seasonsAllowed ?? new List<Season>();
            _growthTimeMonths = growthTimeMonths;
            _description = description;
        }
#endif
    }
}
