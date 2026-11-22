using System.Collections.Generic;
using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Every player-facing content asset the game ships with, found by an
    /// Editor script (ContentCatalogUpdater) so nothing has to be dragged in
    /// by hand. Loaded at startup via Resources.Load. ResourceNodes holds
    /// the Resource type's assets - named to avoid clashing with Unity's
    /// own special "Resources" folder behavior.
    /// </summary>
    [CreateAssetMenu(menuName = "The Emptying/Content/Catalog", fileName = "ContentCatalog")]
    public sealed class ContentCatalog : ScriptableObject
    {
        [SerializeField] private List<UnitAsset> units = new List<UnitAsset>();
        [SerializeField] private List<ResourceAsset> resourceNodes = new List<ResourceAsset>();
        [SerializeField] private List<BuildingAsset> buildings = new List<BuildingAsset>();
        [SerializeField] private List<ItemAsset> items = new List<ItemAsset>();

        public IReadOnlyList<UnitAsset> Units => units;
        public IReadOnlyList<ResourceAsset> ResourceNodes => resourceNodes;
        public IReadOnlyList<BuildingAsset> Buildings => buildings;
        public IReadOnlyList<ItemAsset> Items => items;

#if UNITY_EDITOR
        /// <summary>Editor-only: replaces every list. Called by ContentCatalogUpdater to keep this asset in sync with the project's content assets.</summary>
        public void SetContentsForEditor(List<UnitAsset> units, List<ResourceAsset> resourceNodes, List<BuildingAsset> buildings, List<ItemAsset> items)
        {
            this.units = units ?? new List<UnitAsset>();
            this.resourceNodes = resourceNodes ?? new List<ResourceAsset>();
            this.buildings = buildings ?? new List<BuildingAsset>();
            this.items = items ?? new List<ItemAsset>();
        }
#endif
    }
}
