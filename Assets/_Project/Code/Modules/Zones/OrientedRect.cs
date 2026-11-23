using System;
using System.Collections.Generic;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R12: a rectangle at any angle, in world meters - a center, two
    /// side lengths (SideA runs along RotationDegrees; SideB is
    /// perpendicular to it), and that rotation. Pure math, no Unity
    /// types, so it stays usable from the simulation.
    /// </summary>
    public readonly struct OrientedRect
    {
        public double CenterX { get; }
        public double CenterZ { get; }
        public double SideALength { get; }
        public double SideBLength { get; }
        public double RotationDegrees { get; }

        public OrientedRect(double centerX, double centerZ, double sideALength, double sideBLength, double rotationDegrees)
        {
            CenterX = centerX;
            CenterZ = centerZ;
            SideALength = sideALength;
            SideBLength = sideBLength;
            RotationDegrees = rotationDegrees;
        }

        public double Area => Math.Abs(SideALength * SideBLength);

        /// <summary>The two unit axes this rectangle's sides run along (A then B, B perpendicular to A).</summary>
        public (double X, double Z) AxisA
        {
            get
            {
                double rad = RotationDegrees * Math.PI / 180.0;
                return (Math.Cos(rad), Math.Sin(rad));
            }
        }

        public (double X, double Z) AxisB
        {
            get
            {
                (double x, double z) = AxisA;
                return (-z, x);
            }
        }

        public bool Contains(double worldX, double worldZ)
        {
            (double axisAx, double axisAz) = AxisA;
            (double axisBx, double axisBz) = AxisB;
            double dx = worldX - CenterX;
            double dz = worldZ - CenterZ;

            double localA = dx * axisAx + dz * axisAz;
            double localB = dx * axisBx + dz * axisBz;

            return Math.Abs(localA) <= Math.Abs(SideALength) * 0.5 && Math.Abs(localB) <= Math.Abs(SideBLength) * 0.5;
        }

        /// <summary>The four corners, in world meters, winding order unspecified.</summary>
        public (double X, double Z)[] Corners()
        {
            (double axisAx, double axisAz) = AxisA;
            (double axisBx, double axisBz) = AxisB;
            double halfA = Math.Abs(SideALength) * 0.5;
            double halfB = Math.Abs(SideBLength) * 0.5;

            var corners = new (double X, double Z)[4];
            double[] signsA = { -1, 1, 1, -1 };
            double[] signsB = { -1, -1, 1, 1 };

            for (int i = 0; i < 4; i++)
            {
                double localA = signsA[i] * halfA;
                double localB = signsB[i] * halfB;
                corners[i] = (
                    CenterX + localA * axisAx + localB * axisBx,
                    CenterZ + localA * axisAz + localB * axisBz);
            }

            return corners;
        }

        /// <summary>Separating Axis Theorem overlap test for two (possibly rotated) rectangles.</summary>
        public static bool Overlaps(OrientedRect a, OrientedRect b)
        {
            (double X, double Z)[] cornersA = a.Corners();
            (double X, double Z)[] cornersB = b.Corners();

            foreach ((double X, double Z) axis in new[] { a.AxisA, a.AxisB, b.AxisA, b.AxisB })
            {
                (double minA, double maxA) = ProjectRange(axis, cornersA);
                (double minB, double maxB) = ProjectRange(axis, cornersB);

                if (maxA < minB || maxB < minA)
                {
                    return false;
                }
            }

            return true;
        }

        private static (double Min, double Max) ProjectRange((double X, double Z) axis, IReadOnlyList<(double X, double Z)> corners)
        {
            double min = double.MaxValue;
            double max = double.MinValue;

            foreach ((double x, double z) in corners)
            {
                double projection = x * axis.X + z * axis.Z;
                if (projection < min)
                {
                    min = projection;
                }

                if (projection > max)
                {
                    max = projection;
                }
            }

            return (min, max);
        }
    }
}
