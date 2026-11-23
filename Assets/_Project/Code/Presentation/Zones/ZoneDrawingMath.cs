using System;
using TheEmptying.Modules.Zones;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R12b: turns a press point, the current mouse point and the
    /// camera's yaw into the rectangle being dragged out. The box is
    /// always axis-aligned in the camera's own (right, forward) frame -
    /// a clean, straight box on screen - which is a rectangle rotated to
    /// the camera's yaw in world space. Pure math (no UnityEngine types)
    /// so it's directly unit-testable; the one Unity-specific step (a
    /// camera's Y Euler angle maps to "cameraYawDegrees" by negation,
    /// since Unity's yaw and this rectangle's RotationDegrees turn in
    /// opposite directions) happens at the call site.
    /// </summary>
    public static class ZoneDrawingMath
    {
        public static OrientedRect BuildRect(double startX, double startZ, double currentX, double currentZ, double cameraYawDegrees)
        {
            double rad = cameraYawDegrees * Math.PI / 180.0;

            // The camera's right and forward directions, projected onto the ground, matching Unity's own
            // yaw convention (Quaternion.Euler(0, yaw, 0) * Vector3.forward turns (0,0,1) towards (1,0,0)).
            double rightX = Math.Cos(rad);
            double rightZ = -Math.Sin(rad);
            double forwardX = Math.Sin(rad);
            double forwardZ = Math.Cos(rad);

            double dx = currentX - startX;
            double dz = currentZ - startZ;

            double sideALength = dx * rightX + dz * rightZ;
            double sideBLength = dx * forwardX + dz * forwardZ;

            double centerX = (startX + currentX) * 0.5;
            double centerZ = (startZ + currentZ) * 0.5;

            // OrientedRect.AxisA == (cos, sin) of RotationDegrees; negating the yaw here makes AxisA
            // exactly the camera's right direction (and AxisB, perpendicular by construction, exactly
            // the camera's forward direction) - see OrientedRect.AxisA/AxisB.
            double rotationDegrees = -cameraYawDegrees;

            return new OrientedRect(centerX, centerZ, Math.Abs(sideALength), Math.Abs(sideBLength), rotationDegrees);
        }
    }
}
