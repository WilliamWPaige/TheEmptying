using UnityEngine;
using UnityEngine.UIElements;

namespace TheEmptying.Presentation.Selection
{
    /// <summary>
    /// R8: what the info window shows and highlights, for one selected
    /// thing on the map. Implement this for each clickable kind (person,
    /// resource, and buildings/others later) so the window and the click
    /// router never need to know the specific kind.
    /// </summary>
    public interface ISelectionTarget
    {
        /// <summary>False once this target no longer exists (e.g. the person was removed) - the window and highlight then clear.</summary>
        bool TryGetWorldPosition(out Vector3 worldPosition);

        /// <summary>The highlight's size, in meters (width, depth); (1, 1) for anything without a footprint.</summary>
        Vector2 Footprint { get; }

        /// <summary>Builds the window's body for this target. Called again whenever the window refreshes, so it always reflects live data.</summary>
        void BuildContent(VisualElement container);
    }
}
