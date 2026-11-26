using UnityEngine;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// R20a: a spawned model's own pivot can sit anywhere - its center,
    /// a corner, wherever its artist left it - never assumed to be at
    /// its own base. This measures how far a spawned instance's pivot
    /// sits above the bottom of its own combined render bounds, from
    /// its actual renderers, so any presenter can place the bottom of
    /// whatever it spawns flush on the ground, whatever that model's
    /// pivot - instead of a number hard-coded per shape. The one shared
    /// place every presenter that spawns a world model (or primitive)
    /// calls into for this; see PersonPresenter, TemporaryChunkDecorations,
    /// BuildingPresenter, StartingSiteMarkerPresenter and
    /// StockpileStackPresenter. Ponds are the one exception - they stay
    /// flat on the ground surface by design, so PondsPresenter never
    /// calls this.
    /// </summary>
    public static class GroundPlacement
    {
        /// <summary>
        /// Call once, right after an instance is created and scaled to
        /// its final size (before positioning it for the first time).
        /// The result is a plain vertical distance from the instance's
        /// current pivot (transform.position.y) down to the bottom of
        /// its renderers' combined world-space bounds - unaffected by
        /// where the instance currently sits, or by any yaw (yaw-only,
        /// around the vertical axis) rotation, so it's safe to add
        /// straight onto a target ground-level world Y. 0 if the
        /// instance has no renderers yet.
        /// </summary>
        public static float PivotHeightAboveBase(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return 0f;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return instance.transform.position.y - bounds.min.y;
        }
    }
}
