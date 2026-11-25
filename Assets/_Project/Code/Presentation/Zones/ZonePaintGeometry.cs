using System;
using System.Collections.Generic;
using TheEmptying.Modules.Map;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18b: pure cell geometry for painting - the brush's own footprint,
    /// filling a dragged line without gaps even when the mouse moves fast
    /// between frames, and the "does at least one solid square fit"
    /// check that gates the Create button. No Unity types, so it's
    /// testable on its own.
    /// </summary>
    public static class ZonePaintGeometry
    {
        /// <summary>
        /// The brush's own cells at this anchor cell: a square brush is
        /// anchored at its min-corner cell (the same convention chunk
        /// placements use); a round brush is centered on the anchor
        /// cell's own center, with sizeCells as its diameter.
        /// </summary>
        public static IEnumerable<CellCoord> BrushFootprint(CellCoord anchorCell, BrushShape shape, int sizeCells)
        {
            sizeCells = Math.Max(1, sizeCells);

            if (shape == BrushShape.Square)
            {
                for (int dx = 0; dx < sizeCells; dx++)
                {
                    for (int dz = 0; dz < sizeCells; dz++)
                    {
                        yield return new CellCoord(anchorCell.X + dx, anchorCell.Z + dz);
                    }
                }

                yield break;
            }

            // Round: centered on the anchor cell, diameter = sizeCells (in cell units); a cell counts
            // in if its own center falls within sizeCells/2 of the anchor cell's center.
            double radius = sizeCells * 0.5;
            int span = (int)Math.Ceiling(radius);
            for (int dx = -span; dx <= span; dx++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    double distance = Math.Sqrt((dx + 0.0) * dx + (dz + 0.0) * dz);
                    if (distance <= radius)
                    {
                        yield return new CellCoord(anchorCell.X + dx, anchorCell.Z + dz);
                    }
                }
            }
        }

        /// <summary>
        /// Every cell the brush would touch while its anchor moves in a
        /// straight line from one cell to another, inclusive of both
        /// ends - a dragged line never leaves gaps even when the mouse
        /// moves several cells between two frames.
        /// </summary>
        public static IEnumerable<CellCoord> BrushStroke(CellCoord from, CellCoord to, BrushShape shape, int sizeCells)
        {
            var result = new HashSet<CellCoord>();
            foreach (CellCoord anchor in LineCells(from, to))
            {
                foreach (CellCoord cell in BrushFootprint(anchor, shape, sizeCells))
                {
                    result.Add(cell);
                }
            }

            return result;
        }

        /// <summary>Every cell on a straight line between two cells, inclusive of both ends (Bresenham).</summary>
        public static IEnumerable<CellCoord> LineCells(CellCoord from, CellCoord to)
        {
            int x = from.X;
            int z = from.Z;
            int dx = to.X - from.X;
            int dz = to.Z - from.Z;
            int stepX = Math.Sign(dx);
            int stepZ = Math.Sign(dz);
            dx = Math.Abs(dx);
            dz = Math.Abs(dz);

            yield return new CellCoord(x, z);

            if (dx >= dz)
            {
                int error = dx / 2;
                for (int i = 0; i < dx; i++)
                {
                    error -= dz;
                    if (error < 0)
                    {
                        z += stepZ;
                        error += dx;
                    }

                    x += stepX;
                    yield return new CellCoord(x, z);
                }
            }
            else
            {
                int error = dz / 2;
                for (int i = 0; i < dz; i++)
                {
                    error -= dx;
                    if (error < 0)
                    {
                        x += stepX;
                        error += dz;
                    }

                    z += stepZ;
                    yield return new CellCoord(x, z);
                }
            }
        }

        /// <summary>Whether this cell set contains at least one solid, axis-aligned squareSize x squareSize block.</summary>
        public static bool ContainsSquare(IReadOnlyCollection<CellCoord> cells, int squareSize)
        {
            if (squareSize <= 0 || cells.Count < squareSize * squareSize)
            {
                return false;
            }

            var set = cells as HashSet<CellCoord> ?? new HashSet<CellCoord>(cells);

            foreach (CellCoord cell in set)
            {
                bool allPresent = true;
                for (int dx = 0; dx < squareSize && allPresent; dx++)
                {
                    for (int dz = 0; dz < squareSize && allPresent; dz++)
                    {
                        if (!set.Contains(new CellCoord(cell.X + dx, cell.Z + dz)))
                        {
                            allPresent = false;
                        }
                    }
                }

                if (allPresent)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
