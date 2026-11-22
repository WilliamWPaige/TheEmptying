using System.Collections.Generic;
using TheEmptying.Core.Content;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    [CreateAssetMenu(menuName = "The Emptying/Content/Building Type", fileName = "BuildingType")]
    public sealed class BuildingTypeAsset : ContentAssetBase
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private int _footprintWidth;
        [SerializeField] private int _footprintDepth;
        [SerializeField] private List<GoodAmountAsset> _constructionCost = new List<GoodAmountAsset>();
        [SerializeField] private int _workerSlots;
        [SerializeField] private int _managerSlots;
        [SerializeField] private int _trainerSlots;

        [SerializeField, Tooltip("Allowed in its zones by default. Unchecked means it must be discovered before it can be set up.")]
        private bool _isBasic;

        [SerializeField, Tooltip("Placeable anywhere, regardless of zones (the crown's own buildings). A non-government building type must allow at least one zone.")]
        private bool _isGovernment;

        [SerializeField, Tooltip("3.10b: the founders build this themselves when the town needs it (a saw pit, a camp, a cabin...) - never offered in the player's build menu, even though it's still a government building.")]
        private bool _isFounderBuilt;

        [SerializeField] private List<ZoneAsset> _allowedZones = new List<ZoneAsset>();
        [SerializeField] private bool _isHome;
        [SerializeField] private int _householdsProvided;
        [SerializeField] private SocialClass _homeClass;
        [SerializeField, TextArea] private string _description;

        public override string Id => _id;
        public override string IdPrefix => "building.";
        public override string ContentTypeName => BuiltInContentTypes.BuildingTypeTypeName;

        public string DisplayName => _displayName;
        public int FootprintWidth => _footprintWidth;
        public int FootprintDepth => _footprintDepth;
        public IReadOnlyList<GoodAmountAsset> ConstructionCost => _constructionCost;
        public int WorkerSlots => _workerSlots;
        public int ManagerSlots => _managerSlots;
        public int TrainerSlots => _trainerSlots;
        public bool IsBasic => _isBasic;
        public bool IsGovernment => _isGovernment;
        public bool IsFounderBuilt => _isFounderBuilt;
        public IReadOnlyList<ZoneAsset> AllowedZones => _allowedZones;
        public int HouseholdsProvided => _isHome ? _householdsProvided : 0;
        public SocialClass? HomeClass => _isHome ? _homeClass : (SocialClass?)null;
        public string Description => _description;

        public override IEnumerable<string> ValidateSelf()
        {
            foreach (string problem in base.ValidateSelf())
            {
                yield return problem;
            }

            foreach (GoodAmountAsset cost in _constructionCost)
            {
                if (cost.Good == null)
                {
                    yield return "Construction cost has an entry with no good assigned.";
                }
                else if (cost.Amount < 0)
                {
                    yield return $"Construction cost for '{cost.Good.Id}' must not be negative.";
                }
            }

            foreach (ZoneAsset zone in _allowedZones)
            {
                if (zone == null)
                {
                    yield return "Allowed zones has an empty entry.";
                }
            }

            if (!_isGovernment && _allowedZones.Count == 0)
            {
                yield return "Must allow at least one zone, unless marked government.";
            }
        }

#if UNITY_EDITOR
        public void SetForEditor(
            string id,
            string displayName,
            int footprintWidth,
            int footprintDepth,
            List<GoodAmountAsset> constructionCost,
            int workerSlots,
            int managerSlots,
            int trainerSlots,
            bool isBasic,
            bool isGovernment,
            List<ZoneAsset> allowedZones,
            int householdsProvided,
            SocialClass? homeClass,
            string description,
            bool isFounderBuilt = false)
        {
            _id = id;
            _displayName = displayName;
            _footprintWidth = footprintWidth;
            _footprintDepth = footprintDepth;
            _constructionCost = constructionCost ?? new List<GoodAmountAsset>();
            _workerSlots = workerSlots;
            _managerSlots = managerSlots;
            _trainerSlots = trainerSlots;
            _isBasic = isBasic;
            _isGovernment = isGovernment;
            _allowedZones = allowedZones ?? new List<ZoneAsset>();
            _isHome = homeClass.HasValue;
            _householdsProvided = householdsProvided;
            _homeClass = homeClass ?? default;
            _description = description;
            _isFounderBuilt = isFounderBuilt;
        }
#endif
    }
}
