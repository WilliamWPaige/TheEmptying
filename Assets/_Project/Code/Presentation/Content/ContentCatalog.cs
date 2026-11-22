using UnityEngine;

namespace TheEmptying.Presentation.Content
{
    /// <summary>
    /// Every player-facing content asset the game ships with, found by an
    /// Editor script (ContentCatalogUpdater) so nothing has to be dragged in
    /// by hand. Loaded at startup via Resources.Load.
    ///
    /// Empty since R0 removed the Milestone 3 content types; lists come back
    /// here as each type is rebuilt.
    /// </summary>
    [CreateAssetMenu(menuName = "The Emptying/Content/Catalog", fileName = "ContentCatalog")]
    public sealed class ContentCatalog : ScriptableObject
    {
    }
}
