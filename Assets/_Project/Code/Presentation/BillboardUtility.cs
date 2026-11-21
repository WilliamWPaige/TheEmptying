using UnityEngine;

namespace TheEmptying.Presentation
{
    /// <summary>Shared "face the camera every frame, flat against the screen" rotation for world-space labels (people, buildings).</summary>
    public static class BillboardUtility
    {
        public static void FaceCameraFlat(Transform label, Camera camera)
        {
            label.rotation = camera.transform.rotation;
        }
    }
}
