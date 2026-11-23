using NUnit.Framework;
using TheEmptying.Modules.Zones;
using TheEmptying.Presentation.Zones;

namespace TheEmptying.Tests.EditMode.Presentation
{
    /// <summary>R12b: the press-drag-release rectangle - built from two ground points and the camera's yaw - has the right center, side lengths and rotation, for drags in every direction and for a rotated camera.</summary>
    public class ZoneDrawingMathTests
    {
        private const double Tolerance = 1e-9;

        [Test]
        public void BuildRect_ZeroYaw_DraggedDownRight_IsAxisAligned()
        {
            OrientedRect rect = ZoneDrawingMath.BuildRect(0, 0, 6, 4, cameraYawDegrees: 0);

            Assert.AreEqual(3.0, rect.CenterX, Tolerance);
            Assert.AreEqual(2.0, rect.CenterZ, Tolerance);
            Assert.AreEqual(6.0, rect.SideALength, Tolerance);
            Assert.AreEqual(4.0, rect.SideBLength, Tolerance);
            Assert.AreEqual(0.0, rect.RotationDegrees, Tolerance);
        }

        [Test]
        public void BuildRect_ZeroYaw_DraggedUpLeft_HasSameSizeAsDownRight()
        {
            OrientedRect rect = ZoneDrawingMath.BuildRect(0, 0, -6, -4, cameraYawDegrees: 0);

            Assert.AreEqual(-3.0, rect.CenterX, Tolerance);
            Assert.AreEqual(-2.0, rect.CenterZ, Tolerance);
            Assert.AreEqual(6.0, rect.SideALength, Tolerance);
            Assert.AreEqual(4.0, rect.SideBLength, Tolerance);
        }

        [Test]
        public void BuildRect_ZeroYaw_DraggedUpRight_HasSameSizeAsDownRight()
        {
            OrientedRect rect = ZoneDrawingMath.BuildRect(0, 0, 6, -4, cameraYawDegrees: 0);

            Assert.AreEqual(3.0, rect.CenterX, Tolerance);
            Assert.AreEqual(-2.0, rect.CenterZ, Tolerance);
            Assert.AreEqual(6.0, rect.SideALength, Tolerance);
            Assert.AreEqual(4.0, rect.SideBLength, Tolerance);
        }

        [Test]
        public void BuildRect_ZeroYaw_DraggedDownLeft_HasSameSizeAsDownRight()
        {
            OrientedRect rect = ZoneDrawingMath.BuildRect(0, 0, -6, 4, cameraYawDegrees: 0);

            Assert.AreEqual(-3.0, rect.CenterX, Tolerance);
            Assert.AreEqual(2.0, rect.CenterZ, Tolerance);
            Assert.AreEqual(6.0, rect.SideALength, Tolerance);
            Assert.AreEqual(4.0, rect.SideBLength, Tolerance);
        }

        [Test]
        public void BuildRect_StartAndCurrentAreTheSamePoint_IsDegenerate()
        {
            OrientedRect rect = ZoneDrawingMath.BuildRect(5, 5, 5, 5, cameraYawDegrees: 0);

            Assert.AreEqual(5.0, rect.CenterX, Tolerance);
            Assert.AreEqual(5.0, rect.CenterZ, Tolerance);
            Assert.AreEqual(0.0, rect.SideALength, Tolerance);
            Assert.AreEqual(0.0, rect.SideBLength, Tolerance);
        }

        [Test]
        public void BuildRect_Yaw90_RotatesTheDragAxes()
        {
            // At yaw 90, the camera's "right" points along world -Z and "forward" along world +X -
            // so a drag straight along world +Z (dx=0, dz=8) runs entirely along the camera's right axis.
            OrientedRect rect = ZoneDrawingMath.BuildRect(0, 0, 0, 8, cameraYawDegrees: 90);

            Assert.AreEqual(0.0, rect.CenterX, Tolerance);
            Assert.AreEqual(4.0, rect.CenterZ, Tolerance);
            Assert.AreEqual(8.0, rect.SideALength, Tolerance);
            Assert.AreEqual(0.0, rect.SideBLength, Tolerance);
            Assert.AreEqual(-90.0, rect.RotationDegrees, Tolerance);
        }

        [Test]
        public void BuildRect_Yaw45_ADragAlongWorldXSplitsEvenlyBetweenBothSides()
        {
            OrientedRect rect = ZoneDrawingMath.BuildRect(0, 0, 10, 0, cameraYawDegrees: 45);

            double expected = 10.0 / System.Math.Sqrt(2.0);
            Assert.AreEqual(expected, rect.SideALength, 1e-6);
            Assert.AreEqual(expected, rect.SideBLength, 1e-6);
            Assert.AreEqual(-45.0, rect.RotationDegrees, Tolerance);
        }

        [TestCase(0.0)]
        [TestCase(37.0)]
        [TestCase(-123.0)]
        [TestCase(200.0)]
        public void BuildRect_RotationDegrees_MakesAxisAMatchTheCamerasRightDirection(double cameraYawDegrees)
        {
            OrientedRect rect = ZoneDrawingMath.BuildRect(0, 0, 1, 1, cameraYawDegrees);

            double rad = cameraYawDegrees * System.Math.PI / 180.0;
            double expectedRightX = System.Math.Cos(rad);
            double expectedRightZ = -System.Math.Sin(rad);

            (double axisX, double axisZ) = rect.AxisA;
            Assert.AreEqual(expectedRightX, axisX, 1e-9);
            Assert.AreEqual(expectedRightZ, axisZ, 1e-9);
        }
    }
}
