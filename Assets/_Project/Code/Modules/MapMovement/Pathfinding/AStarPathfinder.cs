using System;
using System.Collections.Generic;

namespace TheEmptying.Modules.MapMovement.Pathfinding
{
    /// <summary>
    /// A* over the grid, 8 directions, no cutting a corner past a blocked
    /// cell, weighted by each cell's movement cost. Ties are always broken
    /// the same way (lowest total cost, then lowest heuristic, then lowest
    /// cell index), so the same input always returns the same path.
    /// One instance per simulation (owned by the movement module): its
    /// scratch buffers are reused across searches, but never shared as
    /// global mutable state between simulations.
    /// </summary>
    public sealed class AStarPathfinder
    {
        private const float Sqrt2 = 1.4142135f;

        /// <summary>
        /// A tiny inflation on the heuristic used only to order the open set,
        /// not to compute real costs. On a mostly-open grid, many cells tie
        /// for the lowest f-score (any cell on any shortest path), and without
        /// a nudge toward the goal A* explores a "diamond" of wasted nodes
        /// proportional to distance squared. This keeps results effectively
        /// optimal (within a fraction of a percent) while collapsing that
        /// diamond back down to roughly the path length.
        /// </summary>
        private const float HeuristicWeight = 1.001f;

        // Fixed iteration order: E, NE, N, NW, W, SW, S, SE.
        private static readonly (int Dx, int Dy)[] Directions =
        {
            (1, 0), (1, 1), (0, 1), (-1, 1),
            (-1, 0), (-1, -1), (0, -1), (1, -1)
        };

        // Reused across calls instead of allocating grid-sized arrays (and a
        // grid-sized heap) on every search: a path search on a mostly-open,
        // very large map only ever touches a small fraction of its cells, so
        // allocating proportional to the whole grid every time is wasteful
        // enough to matter with hundreds of movers re-pathing every tick. A
        // stamp marks which cells belong to the current search, so "reset
        // before a search" is O(1) instead of O(cell count).
        private float[] _gScore = Array.Empty<float>();
        private int[] _cameFrom = Array.Empty<int>();
        private int[] _touchedStamp = Array.Empty<int>();
        private bool[] _closed = Array.Empty<bool>();
        private int _currentStamp;
        private readonly BinaryMinHeap _heap = new BinaryMinHeap(256);

        public PathResult FindPath(MapGrid grid, GridPoint start, GridPoint goal)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (!grid.InBounds(start) || !grid.InBounds(goal) || !grid.IsPassable(goal))
            {
                return PathResult.NoPath;
            }

            if (start == goal)
            {
                return PathResult.Success(new List<GridPoint> { start });
            }

            if (!grid.IsPassable(start))
            {
                return PathResult.NoPath;
            }

            int width = grid.Width;
            int cellCount = width * grid.Depth;
            EnsureBufferCapacity(cellCount);

            _currentStamp++;
            int stamp = _currentStamp;

            int startIndex = grid.IndexOf(start);
            int goalIndex = grid.IndexOf(goal);

            _heap.Clear();
            _gScore[startIndex] = 0f;
            _touchedStamp[startIndex] = stamp;
            _closed[startIndex] = false;

            float hStart = Heuristic(start, goal);
            _heap.Push(hStart * HeuristicWeight, hStart, startIndex);

            while (_heap.Count > 0)
            {
                int currentIndex = _heap.PopMin();

                if (_touchedStamp[currentIndex] == stamp && _closed[currentIndex])
                {
                    continue; // a stale, superseded heap entry
                }

                if (currentIndex == goalIndex)
                {
                    return PathResult.Success(ReconstructPath(_cameFrom, width, startIndex, goalIndex));
                }

                _closed[currentIndex] = true;
                var current = new GridPoint(currentIndex % width, currentIndex / width);

                for (int d = 0; d < Directions.Length; d++)
                {
                    (int dx, int dy) = Directions[d];
                    var neighbor = new GridPoint(current.X + dx, current.Y + dy);

                    if (!grid.IsPassable(neighbor))
                    {
                        continue;
                    }

                    bool isDiagonal = dx != 0 && dy != 0;
                    if (isDiagonal)
                    {
                        var sideA = new GridPoint(current.X + dx, current.Y);
                        var sideB = new GridPoint(current.X, current.Y + dy);
                        if (!grid.IsPassable(sideA) || !grid.IsPassable(sideB))
                        {
                            continue; // no cutting the corner past a blocked cell
                        }
                    }

                    int neighborIndex = grid.IndexOf(neighbor);
                    bool neighborTouched = _touchedStamp[neighborIndex] == stamp;
                    if (neighborTouched && _closed[neighborIndex])
                    {
                        continue;
                    }

                    // The heuristic assumes no cell ever costs less than 1 to enter.
                    // True while every cell's cost is 1; will need revisiting once
                    // roads or other sub-1 terrain costs exist.
                    float stepBase = isDiagonal ? Sqrt2 : 1f;
                    float tentativeG = _gScore[currentIndex] + stepBase * grid.GetMovementCost(neighbor);

                    if (!neighborTouched || tentativeG < _gScore[neighborIndex])
                    {
                        _gScore[neighborIndex] = tentativeG;
                        _touchedStamp[neighborIndex] = stamp;
                        _closed[neighborIndex] = false;
                        _cameFrom[neighborIndex] = currentIndex;

                        float h = Heuristic(neighbor, goal);
                        _heap.Push(tentativeG + h * HeuristicWeight, h, neighborIndex);
                    }
                }
            }

            return PathResult.NoPath;
        }

        private void EnsureBufferCapacity(int cellCount)
        {
            if (_gScore.Length >= cellCount)
            {
                return;
            }

            _gScore = new float[cellCount];
            _cameFrom = new int[cellCount];
            _touchedStamp = new int[cellCount];
            _closed = new bool[cellCount];
            _currentStamp = 0;
        }

        /// <summary>Octile distance: admissible for 8-directional movement at a minimum cost of 1 per cell.</summary>
        private static float Heuristic(GridPoint from, GridPoint to)
        {
            int dx = Math.Abs(from.X - to.X);
            int dy = Math.Abs(from.Y - to.Y);
            int straight = Math.Abs(dx - dy);
            int diagonal = Math.Min(dx, dy);
            return straight + diagonal * Sqrt2;
        }

        private static List<GridPoint> ReconstructPath(int[] cameFrom, int width, int startIndex, int goalIndex)
        {
            var cells = new List<GridPoint>();
            int index = goalIndex;
            while (index != -1)
            {
                cells.Add(new GridPoint(index % width, index / width));
                if (index == startIndex)
                {
                    break;
                }

                index = cameFrom[index];
            }

            cells.Reverse();
            return cells;
        }

        /// <summary>A minimal array-backed binary min-heap keyed by (F, H, CellIndex), all ascending - a fully deterministic tie-break with no dependency on hashing or insertion order.</summary>
        private sealed class BinaryMinHeap
        {
            private float[] _f;
            private float[] _h;
            private int[] _cellIndex;
            private int _count;

            public BinaryMinHeap(int initialCapacity)
            {
                int capacity = Math.Max(16, initialCapacity / 4);
                _f = new float[capacity];
                _h = new float[capacity];
                _cellIndex = new int[capacity];
            }

            public int Count => _count;

            public void Clear() => _count = 0;

            public void Push(float f, float h, int cellIndex)
            {
                EnsureCapacity(_count + 1);
                _f[_count] = f;
                _h[_count] = h;
                _cellIndex[_count] = cellIndex;
                SiftUp(_count);
                _count++;
            }

            public int PopMin()
            {
                int result = _cellIndex[0];
                _count--;
                _f[0] = _f[_count];
                _h[0] = _h[_count];
                _cellIndex[0] = _cellIndex[_count];
                SiftDown(0);
                return result;
            }

            private bool IsLess(int a, int b)
            {
                if (_f[a] != _f[b])
                {
                    return _f[a] < _f[b];
                }

                if (_h[a] != _h[b])
                {
                    return _h[a] < _h[b];
                }

                return _cellIndex[a] < _cellIndex[b];
            }

            private void Swap(int a, int b)
            {
                (_f[a], _f[b]) = (_f[b], _f[a]);
                (_h[a], _h[b]) = (_h[b], _h[a]);
                (_cellIndex[a], _cellIndex[b]) = (_cellIndex[b], _cellIndex[a]);
            }

            private void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!IsLess(index, parent))
                    {
                        break;
                    }

                    Swap(index, parent);
                    index = parent;
                }
            }

            private void SiftDown(int index)
            {
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;

                    if (left < _count && IsLess(left, smallest))
                    {
                        smallest = left;
                    }

                    if (right < _count && IsLess(right, smallest))
                    {
                        smallest = right;
                    }

                    if (smallest == index)
                    {
                        break;
                    }

                    Swap(index, smallest);
                    index = smallest;
                }
            }

            private void EnsureCapacity(int minimum)
            {
                if (_f.Length >= minimum)
                {
                    return;
                }

                int newCapacity = Math.Max(_f.Length * 2, minimum);
                Array.Resize(ref _f, newCapacity);
                Array.Resize(ref _h, newCapacity);
                Array.Resize(ref _cellIndex, newCapacity);
            }
        }
    }
}
