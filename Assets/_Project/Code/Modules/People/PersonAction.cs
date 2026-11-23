using System.Collections.Generic;
using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// R10: a person's one current action - its kind, why it's happening
    /// (the "thought"), when it started, and its progress toward an end.
    /// Idle and Walk share this one shape (an unused field just stays at
    /// its default for the other kind) so the action system, the info
    /// window and saving never need to know which kind they're holding.
    /// </summary>
    public sealed class PersonAction
    {
        public ActionKind Kind { get; set; }

        /// <summary>A short, plain sentence saying why - e.g. "Nothing to do, stretching my legs."</summary>
        public string Thought { get; set; } = string.Empty;

        public long StartTick { get; set; }

        /// <summary>Idle only: how long the wait lasts, in ticks - rolled once when the action starts.</summary>
        public long DurationTicks { get; set; }

        /// <summary>Walk only: the cell being walked to (reserved for this person for as long as this action lasts).</summary>
        public CellCoord TargetCell { get; set; }

        /// <summary>Walk only: the cells still to reach, in order, not including the starting cell. Empty if no path could be found.</summary>
        public List<CellCoord> Path { get; set; } = new List<CellCoord>();

        /// <summary>Walk only: the index into Path of the next cell to reach.</summary>
        public int NextPathIndex { get; set; }

        /// <summary>Walk only: meters covered so far along Path.</summary>
        public double DistanceTraveledMeters { get; set; }

        /// <summary>Walk only: the path's total length in meters, fixed once the path is found. Zero (and so already "finished") when no path was found.</summary>
        public double TotalDistanceMeters { get; set; }

        /// <summary>0 at the start, 1 once the action is finished - Idle by elapsed ticks, Walk by distance covered.</summary>
        public double Progress
        {
            get
            {
                if (Kind == ActionKind.Walk)
                {
                    return TotalDistanceMeters <= 0 ? 1.0 : System.Math.Min(1.0, DistanceTraveledMeters / TotalDistanceMeters);
                }

                return DurationTicks <= 0 ? 1.0 : System.Math.Min(1.0, (double)(CurrentTickForProgress - StartTick) / DurationTicks);
            }
        }

        /// <summary>Set by the action system each time it runs, purely so Progress (a read-only property) can compute an Idle action's elapsed share without a separate method call.</summary>
        public long CurrentTickForProgress { get; set; }

        public bool HasEnded => Kind == ActionKind.Walk
            ? (TotalDistanceMeters <= 0 || NextPathIndex >= Path.Count)
            : (DurationTicks <= 0 || CurrentTickForProgress - StartTick >= DurationTicks);
    }
}
