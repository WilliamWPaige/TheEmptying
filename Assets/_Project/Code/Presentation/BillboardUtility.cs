using UnityEngine;

namespace TheEmptying.Presentation
{
    /// <summary>Shared "face the camera every frame, stay upright" rotation for world-space labels (people, buildings).</summary>
    public static class BillboardUtility
    {
        public static void FaceCameraUpright(Transform label, Camera camera)
        {
            Vector3 toCamera = label.position - camera.transform.position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude > 0.0001f)
            {
                label.rotation = Quaternion.LookRotation(toCamera);
            }
        }
    }
}
