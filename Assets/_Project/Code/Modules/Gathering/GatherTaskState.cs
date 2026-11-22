using TheEmptying.Core.Entities;
using TheEmptying.Modules.MapMovement;

namespace TheEmptying.Modules.Gathering
{
    /// <summary>One jobless person's current hand-gathering task.</summary>
    public sealed class GatherTaskState
    {
        public GatherTaskKind Kind;

        /// <summary>For Build: the construction site being worked.</summary>
        public EntityId? TargetSite;

        /// <summary>For Woodcut and Quarry: the standing tree/outcrop being harvested. For Farm: unused (Spot is a plain cell near the stockpile, unrelated to map resources).</summary>
        public EntityId? ResourceNodeId;

        /// <summary>For Build: the chosen edge cell. For Woodcut/Quarry: the resource node's own cell. For Farm: the chosen spot within radius of the stockpile.</summary>
        public GridPoint Spot;

        /// <summary>For Woodcut and Quarry: skill-adjusted hours accumulated toward the next unit.</summary>
        public double WorkHoursAccumulated;

        /// <summary>For Farm: whether a crop is currently growing at Spot.</summary>
        public bool Planted;

        /// <summary>For Farm: the tick the planted crop is ready to harvest.</summary>
        public long ReadyAtTick;

        /// <summary>3.10a: set instead of a real task when Woodcut/Quarry was wanted but no standing tree/outcrop is in reach - DescribeIdleReason surfaces this; cleared the moment a real task is assigned.</summary>
        public string NoResourceReason;

        /// <summary>3.10b3, for Haul: which leg of the round trip - walking to the stockpile, or walking to TargetSite with a load.</summary>
        public HaulPhase HaulPhase;

        /// <summary>3.10b3, for Haul: the good being fetched; null until chosen at pickup.</summary>
        public string HaulGoodId;

        /// <summary>3.10b3, for Haul: how much is currently being carried - 0 until picked up at the stockpile, back to 0 once dropped off.</summary>
        public int HaulAmount;

        /// <summary>3.10b4: the plain-English reason this task was assigned - set whenever PostTaskChanged fires, for the debug readout and state dump.</summary>
        public string LastAssignReason;
    }
}
