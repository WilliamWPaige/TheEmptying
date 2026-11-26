using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Randomness;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.Zones
{
    /// <summary>
    /// R20: builds a stockpile's fixed list of 2x2 m stack spots from its
    /// painted cells, once, at zone creation. Uniform lays spots out on a
    /// plain north-south grid (no rotation, so no randomness is needed -
    /// the same cells always give the same grid). Natural scatters small,
    /// rotated patches (each its own little uniform grid) so no two spots
    /// overlap and every spot's cells are fully inside the paint, reading
    /// random patch angle/size/position from the given stream - callers
    /// must pass a stream named for this one zone (see
    /// ZonesModule.CreateZone), so the same zone always gets the same
    /// spots, reproducibly after a load.
    /// </summary>
    internal static class StockSpotLayoutGenerator
    {
        public static List<StockSpot> Generate(
            string layoutId,
            HashSet<CellCoord> paintedCells,
            double cellSizeMeters,
            double spotSizeMeters,
            int naturalPatchMinSpotsPerSide,
            int naturalPatchMaxSpotsPerSide,
            double naturalPatchMinAngleDegrees,
            double naturalPatchMaxAngleDegrees,
            int naturalMaxPlacementAttempts,
            SimRandom random)
        {
            if (paintedCells.Count == 0)
            {
                return new List<StockSpot>();
            }

            return layoutId == ZonesModule.LayoutUniformId
                ? GenerateUniform(paintedCells, cellSizeMeters, spotSizeMeters)
                : GenerateNatural(
                    paintedCells,
                    cellSizeMeters,
                    spotSizeMeters,
                    naturalPatchMinSpotsPerSide,
                    naturalPatchMaxSpotsPerSide,
                    naturalPatchMinAngleDegrees,
                    naturalPatchMaxAngleDegrees,
                    naturalMaxPlacementAttempts,
                    random);
        }

        private static List<StockSpot> GenerateUniform(HashSet<CellCoord> paintedCells, double cellSizeMeters, double spotSizeMeters)
        {
            var spots = new List<StockSpot>();
            (int minX, int minZ, int maxX, int maxZ) = Bounds(paintedCells);

            double originX = minX * cellSizeMeters;
            double originZ = minZ * cellSizeMeters;
            double spanX = (maxX - minX + 1) * cellSizeMeters;
            double spanZ = (maxZ - minZ + 1) * cellSizeMeters;

            for (double z = originZ + spotSizeMeters * 0.5; z < originZ + spanZ; z += spotSizeMeters)
            {
                for (double x = originX + spotSizeMeters * 0.5; x < originX + spanX; x += spotSizeMeters)
                {
                    HashSet<CellCoord> covered = CellRasterizer.RasterizeRectangleToCells(x, z, spotSizeMeters, spotSizeMeters, 0, cellSizeMeters);
                    if (covered.Count > 0 && covered.All(paintedCells.Contains))
                    {
                        spots.Add(new StockSpot { CenterX = x, CenterZ = z, RotationDegrees = 0 });
                    }
                }
            }

            return spots;
        }

        private static List<StockSpot> GenerateNatural(
            HashSet<CellCoord> paintedCells,
            double cellSizeMeters,
            double spotSizeMeters,
            int minSpotsPerSide,
            int maxSpotsPerSide,
            double minAngleDegrees,
            double maxAngleDegrees,
            int maxPlacementAttempts,
            SimRandom random)
        {
            var spots = new List<StockSpot>();
            var placedCovered = new List<HashSet<CellCoord>>();

            (int minX, int minZ, int maxX, int maxZ) = Bounds(paintedCells);
            double minWorldX = minX * cellSizeMeters;
            double minWorldZ = minZ * cellSizeMeters;
            double maxWorldX = (maxX + 1) * cellSizeMeters;
            double maxWorldZ = (maxZ + 1) * cellSizeMeters;

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                double angleDegrees = minAngleDegrees + random.NextFloat() * (maxAngleDegrees - minAngleDegrees);
                int patchWidth = random.NextInt(minSpotsPerSide, maxSpotsPerSide + 1);
                int patchHeight = random.NextInt(minSpotsPerSide, maxSpotsPerSide + 1);
                double anchorX = minWorldX + random.NextFloat() * (maxWorldX - minWorldX);
                double anchorZ = minWorldZ + random.NextFloat() * (maxWorldZ - minWorldZ);

                List<(double X, double Z)> centers = PatchSpotCenters(anchorX, anchorZ, angleDegrees, patchWidth, patchHeight, spotSizeMeters);
                var candidateCovered = new List<HashSet<CellCoord>>(centers.Count);
                bool fits = true;

                foreach ((double x, double z) in centers)
                {
                    HashSet<CellCoord> covered = CellRasterizer.RasterizeRectangleToCells(x, z, spotSizeMeters, spotSizeMeters, angleDegrees, cellSizeMeters);
                    if (covered.Count == 0 || !covered.All(paintedCells.Contains) || OverlapsAny(covered, placedCovered) || OverlapsAny(covered, candidateCovered))
                    {
                        fits = false;
                        break;
                    }

                    candidateCovered.Add(covered);
                }

                if (!fits)
                {
                    continue;
                }

                foreach ((double x, double z) in centers)
                {
                    spots.Add(new StockSpot { CenterX = x, CenterZ = z, RotationDegrees = angleDegrees });
                }

                placedCovered.AddRange(candidateCovered);
            }

            return spots;
        }

        private static List<(double X, double Z)> PatchSpotCenters(double anchorX, double anchorZ, double angleDegrees, int patchWidth, int patchHeight, double spotSizeMeters)
        {
            double rad = angleDegrees * System.Math.PI / 180.0;
            double axisUx = System.Math.Cos(rad);
            double axisUz = System.Math.Sin(rad);
            double axisVx = -axisUz;
            double axisVz = axisUx;

            var centers = new List<(double, double)>(patchWidth * patchHeight);
            for (int i = 0; i < patchWidth; i++)
            {
                double localX = (i - (patchWidth - 1) * 0.5) * spotSizeMeters;
                for (int j = 0; j < patchHeight; j++)
                {
                    double localZ = (j - (patchHeight - 1) * 0.5) * spotSizeMeters;
                    double worldX = anchorX + localX * axisUx + localZ * axisVx;
                    double worldZ = anchorZ + localX * axisUz + localZ * axisVz;
                    centers.Add((worldX, worldZ));
                }
            }

            return centers;
        }

        private static bool OverlapsAny(HashSet<CellCoord> covered, List<HashSet<CellCoord>> placed)
        {
            foreach (HashSet<CellCoord> other in placed)
            {
                if (covered.Overlaps(other))
                {
                    return true;
                }
            }

            return false;
        }

        private static (int MinX, int MinZ, int MaxX, int MaxZ) Bounds(HashSet<CellCoord> cells)
        {
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            foreach (CellCoord cell in cells)
            {
                if (cell.X < minX) minX = cell.X;
                if (cell.X > maxX) maxX = cell.X;
                if (cell.Z < minZ) minZ = cell.Z;
                if (cell.Z > maxZ) maxZ = cell.Z;
            }

            return (minX, minZ, maxX, maxZ);
        }
    }
}
