using System.Collections.Generic;
using TheEmptying.Modules.Map;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Modules.People
{
    /// <summary>
    /// R10: a person's one current action - its kind, why it's happening
    /// (the "thought"), when it started, and its progress toward an end.
    /// Idle, Walk and InsideBuilding (R11) share this one shape (an unused
    /// field just stays at its default for the other kinds) so the action
    /// system, the info window and saving never need to know which kind
    /// they're holding.
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

        /// <summary>InsideBuilding only: which building they're waiting in.</summary>
        public EntityId BuildingId { get; set; }

        /// <summary>0 at the start, 1 once the action is finished - Idle by elapsed ticks, Walk by distance covered; always 0 for InsideBuilding, which has no notion of progress (only the keep-exit timer ends it).</summary>
        public double Progress
        {
            get
            {
                if (Kind == ActionKind.Walk)
                {
                    return TotalDistanceMeters <= 0 ? 1.0 : System.Math.Min(1.0, DistanceTraveledMeters / TotalDistanceMeters);
                }

                if (Kind == ActionKind.InsideBuilding)
                {
                    return 0.0;
                }

                return DurationTicks <= 0 ? 1.0 : System.Math.Min(1.0, (double)(CurrentTickForProgress - StartTick) / DurationTicks);
            }
        }

        /// <summary>Set by the action system each time it runs, purely so Progress (a read-only property) can compute an Idle action's elapsed share without a separate method call.</summary>
        public long CurrentTickForProgress { get; set; }

        /// <summary>InsideBuilding never ends on its own - only PeopleModule's keep-exit timer replaces it, by directly assigning a new PersonAction.</summary>
        public bool HasEnded
        {
            get
            {
                if (Kind == ActionKind.Walk)
                {
                    return TotalDistanceMeters <= 0 || NextPathIndex >= Path.Count;
                }

                if (Kind == ActionKind.InsideBuilding)
                {
                    return false;
                }

                return DurationTicks <= 0 || CurrentTickForProgress - StartTick >= DurationTicks;
            }
        }
    }
}
