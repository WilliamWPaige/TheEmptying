using System.Collections.Generic;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Every player-facing content asset the game ships with, found by an
    /// Editor script (ContentCatalogUpdater) so nothing has to be dragged in
    /// by hand. Loaded at startup via Resources.Load.
    /// </summary>
    [CreateAssetMenu(menuName = "The Emptying/Content/Catalog", fileName = "ContentCatalog")]
    public sealed class ContentCatalog : ScriptableObject
    {
        [SerializeField] private List<GoodAsset> _goods = new List<GoodAsset>();
        [SerializeField] private List<NeedAsset> _needs = new List<NeedAsset>();
        [SerializeField] private List<ZoneAsset> _zones = new List<ZoneAsset>();
        [SerializeField] private List<BuildingTypeAsset> _buildingTypes = new List<BuildingTypeAsset>();
        [SerializeField] private List<RecipeAsset> _recipes = new List<RecipeAsset>();

        public IReadOnlyList<GoodAsset> Goods => _goods;
        public IReadOnlyList<NeedAsset> Needs => _needs;
        public IReadOnlyList<ZoneAsset> Zones => _zones;
        public IReadOnlyList<BuildingTypeAsset> BuildingTypes => _buildingTypes;
        public IReadOnlyList<RecipeAsset> Recipes => _recipes;

#if UNITY_EDITOR
        /// <summary>Editor-only: replaces every list. Called by ContentCatalogUpdater to keep this asset in sync with the project's content assets.</summary>
        public void SetContentsForEditor(
            List<GoodAsset> goods,
            List<NeedAsset> needs,
            List<ZoneAsset> zones,
            List<BuildingTypeAsset> buildingTypes,
            List<RecipeAsset> recipes)
        {
            _goods = goods ?? new List<GoodAsset>();
            _needs = needs ?? new List<NeedAsset>();
            _zones = zones ?? new List<ZoneAsset>();
            _buildingTypes = buildingTypes ?? new List<BuildingTypeAsset>();
            _recipes = recipes ?? new List<RecipeAsset>();
        }
#endif
    }
}
