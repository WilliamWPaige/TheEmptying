using System;
using System.Collections.Generic;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// R10: A* over the cell grid, 8-directional, stepping around any cell
    /// cellCost reports as blocked (double.PositiveInfinity - trees,
    /// stones, grub mounds) and preferring to detour around any cell it
    /// reports as merely costly (R17: pond water) when a reasonable
    /// detour exists. Bounded to a square box of maxDistanceMeters around
    /// the start, so a single search can never run away over a big,
    /// mostly-empty map. Purely deterministic - fixed step order, no
    /// dictionary-order dependence, no randomness - so the same start,
    /// target and obstacles always give the same path.
    /// </summary>
    public static class CellPathFinder
    {
        private static readonly (int Dx, int Dz, double Cost)[] Steps =
        {
            (1, 0, 1.0), (-1, 0, 1.0), (0, 1, 1.0), (0, -1, 1.0),
            (1, 1, 1.4142135623730951), (1, -1, 1.4142135623730951),
            (-1, 1, 1.4142135623730951), (-1, -1, 1.4142135623730951)
        };

        /// <summary>
        /// Null if start and target are the same cell, or the target itself
        /// is blocked (cellCost returns double.PositiveInfinity). Otherwise
        /// the cells from (not including) start to (including) target, or
        /// an empty list if the target is genuinely unreachable within
        /// maxDistanceMeters. cellCost's return multiplies a step's own
        /// distance cost - 1.0 is normal ground, anything higher (R17:
        /// pond water) is wadeable but avoided when a same-length-ish
        /// detour exists, since A* will always prefer the cheaper total
        /// path.
        /// </summary>
        public static List<CellCoord> FindPath(CellCoord start, CellCoord target, double maxDistanceMeters, double cellSizeMeters, Func<CellCoord, double> cellCost)
        {
            if (start == target || double.IsPositiveInfinity(cellCost(target)))
            {
                return null;
            }

            int maxCells = (int)Math.Ceiling(maxDistanceMeters / Math.Max(cellSizeMeters, 0.0001));

            var open = new List<CellCoord> { start };
            var cameFrom = new Dictionary<CellCoord, CellCoord>();
            var gScore = new Dictionary<CellCoord, double> { [start] = 0 };
            var fScore = new Dictionary<CellCoord, double> { [start] = Heuristic(start, target) };
            var closed = new HashSet<CellCoord>();

            while (open.Count > 0)
            {
                CellCoord current = PopLowestF(open, fScore);

                if (current == target)
                {
                    return ReconstructPath(cameFrom, current);
                }

                closed.Add(current);

                foreach ((int dx, int dz, double cost) in Steps)
                {
                    var neighbor = new CellCoord(current.X + dx, current.Z + dz);

                    if (closed.Contains(neighbor) || OutOfRange(neighbor, start, maxCells))
                    {
                        continue;
                    }

                    double neighborCost = cellCost(neighbor);
                    if (double.IsPositiveInfinity(neighborCost))
                    {
                        continue;
                    }

                    double tentativeG = gScore[current] + cost * neighborCost;
                    if (gScore.TryGetValue(neighbor, out double existingG) && tentativeG >= existingG)
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, target);

                    if (!open.Contains(neighbor))
                    {
                        open.Add(neighbor);
                    }
                }
            }

            return new List<CellCoord>();
        }

        private static bool OutOfRange(CellCoord cell, CellCoord start, int maxCells)
        {
            return Math.Abs(cell.X - start.X) > maxCells || Math.Abs(cell.Z - start.Z) > maxCells;
        }

        private static double Heuristic(CellCoord a, CellCoord b)
        {
            double dx = a.X - b.X;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static CellCoord PopLowestF(List<CellCoord> open, Dictionary<CellCoord, double> fScore)
        {
            int bestIndex = 0;
            double bestScore = fScore[open[0]];
            for (int i = 1; i < open.Count; i++)
            {
                double score = fScore[open[i]];
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            CellCoord best = open[bestIndex];
            open.RemoveAt(bestIndex);
            return best;
        }

        private static List<CellCoord> ReconstructPath(Dictionary<CellCoord, CellCoord> cameFrom, CellCoord current)
        {
            var path = new List<CellCoord> { current };
            while (cameFrom.TryGetValue(current, out CellCoord previous))
            {
                current = previous;
                path.Add(current);
            }

            path.Reverse();
            path.RemoveAt(0); // drop the start cell itself
            return path;
        }
    }
}
