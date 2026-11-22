using System;
using UnityEngine;

namespace TheEmptying.Presentation.World
{
    /// <summary>
    /// R6: keeps the camera near Unity's float-precision origin on a big
    /// map by shifting the RENDERED world when the camera drifts far from
    /// it. Simulation (true world) positions are never touched - only
    /// Unity Transforms that something has chosen to shift react, and all
    /// of them move by the same delta in the same frame, so nothing
    /// visually jitters anywhere on the map.
    /// </summary>
    public sealed class FloatingOrigin
    {
        private const float ShiftThresholdMeters = 2000f;

        /// <summary>The true world position that currently renders at Unity's (0,0,0): render = world - CurrentOffset.</summary>
        public Vector3 CurrentOffset { get; private set; }

        /// <summary>Raised with the delta every shifted Transform must subtract from its own position.</summary>
        public event Action<Vector3> Shifted;

        public Vector3 WorldToRender(Vector3 worldPosition) => worldPosition - CurrentOffset;
        public Vector3 RenderToWorld(Vector3 renderPosition) => renderPosition + CurrentOffset;

        /// <summary>Call once per frame with the camera's true world position. Re-centers (and notifies every subscriber) only once the camera has drifted past the threshold.</summary>
        public void CheckAndShift(Vector3 cameraWorldPosition)
        {
            Vector3 renderPosition = WorldToRender(cameraWorldPosition);
            if (renderPosition.sqrMagnitude < ShiftThresholdMeters * ShiftThresholdMeters)
            {
                return;
            }

            CurrentOffset += renderPosition;
            Shifted?.Invoke(renderPosition);
        }
    }
}
