using System;
using System.Collections.Generic;
using TheEmptying.Modules.Map;

namespace TheEmptying.Presentation.Zones
{
    /// <summary>
    /// R18b: the zoning menu's Undo stack - one snapshot pushed per
    /// stroke (press to release, paint or erase; Erase all counts as a
    /// stroke too), capped at a maximum depth. Pure and testable on its
    /// own, independent of the menu's UI.
    /// </summary>
    public sealed class ZonePaintUndoStack
    {
        private readonly List<HashSet<CellCoord>> _stack = new List<HashSet<CellCoord>>();
        private readonly int _maxSteps;

        public ZonePaintUndoStack(int maxSteps)
        {
            _maxSteps = Math.Max(1, maxSteps);
        }

        public int Count => _stack.Count;
        public int MaxSteps => _maxSteps;

        /// <summary>Snapshots the given cells as the state to return to; drops the oldest step once past MaxSteps.</summary>
        public void Push(IEnumerable<CellCoord> cells)
        {
            _stack.Add(new HashSet<CellCoord>(cells));
            while (_stack.Count > _maxSteps)
            {
                _stack.RemoveAt(0);
            }
        }

        /// <summary>Pops and returns the most recent snapshot; false (and no change) if there is nothing to undo.</summary>
        public bool TryUndo(out HashSet<CellCoord> previous)
        {
            if (_stack.Count == 0)
            {
                previous = null;
                return false;
            }

            previous = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            return true;
        }

        public void Clear()
        {
            _stack.Clear();
        }
    }
}
