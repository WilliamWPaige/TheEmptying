using System;
using System.Collections.Generic;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// Pure geometry: which grid cells a rotated rectangle (centered in
    /// world meters, with a given rotation) covers, by its cells' own
    /// centers falling inside it. Shared by the old-save rectangle-zone
    /// migration (ZonesModule) and the R20 stockpile spot layout
    /// generator (StockSpotLayoutGenerator), which both need to turn a
    /// centered, possibly-rotated rectangle into the set of cells it
    /// occupies.
    /// </summary>
    internal static class CellRasterizer
    {
        public static HashSet<CellCoord> RasterizeRectangleToCells(double centerX, double centerZ, double sideALength, double sideBLength, double rotationDegrees, double cellSizeMeters)
        {
            double rad = rotationDegrees * Math.PI / 180.0;
            double axisAx = Math.Cos(rad);
            double axisAz = Math.Sin(rad);
            double axisBx = -axisAz;
            double axisBz = axisAx;
            double halfA = Math.Abs(sideALength) * 0.5;
            double halfB = Math.Abs(sideBLength) * 0.5;

            double extentX = Math.Abs(axisAx) * halfA + Math.Abs(axisBx) * halfB;
            double extentZ = Math.Abs(axisAz) * halfA + Math.Abs(axisBz) * halfB;

            int minCellX = (int)Math.Floor((centerX - extentX) / cellSizeMeters);
            int maxCellX = (int)Math.Ceiling((centerX + extentX) / cellSizeMeters);
            int minCellZ = (int)Math.Floor((centerZ - extentZ) / cellSizeMeters);
            int maxCellZ = (int)Math.Ceiling((centerZ + extentZ) / cellSizeMeters);

            var cells = new HashSet<CellCoord>();
            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                for (int cz = minCellZ; cz <= maxCellZ; cz++)
                {
                    double worldX = (cx + 0.5) * cellSizeMeters;
                    double worldZ = (cz + 0.5) * cellSizeMeters;
                    double dx = worldX - centerX;
                    double dz = worldZ - centerZ;
                    double localA = dx * axisAx + dz * axisAz;
                    double localB = dx * axisBx + dz * axisBz;

                    if (Math.Abs(localA) <= halfA && Math.Abs(localB) <= halfB)
                    {
                        cells.Add(new CellCoord(cx, cz));
                    }
                }
            }

            return cells;
        }
    }
}
