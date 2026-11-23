using TheEmptying.Modules.Map;

namespace TheEmptying.Modules.Buildings
{
    /// <summary>
    /// R11: one placed building - its type, footprint and position. The
    /// footprint's min cell and door cell are fixed at placement time
    /// (never recomputed), since the keep can never move.
    /// </summary>
    public sealed class BuildingState
    {
        public string ContentId { get; }
        public double WorldX { get; }
        public double WorldZ { get; }
        public int FootprintWidth { get; }
        public int FootprintDepth { get; }

        /// <summary>The footprint's lowest-X/lowest-Z corner cell.</summary>
        public CellCoord MinCell { get; }

        /// <summary>The one cell people use to go in and out - always walkable, even though every other footprint cell blocks.</summary>
        public CellCoord DoorCell { get; }

        public BuildingState(string contentId, double worldX, double worldZ, int footprintWidth, int footprintDepth, CellCoord minCell, CellCoord doorCell)
        {
            ContentId = contentId;
            WorldX = worldX;
            WorldZ = worldZ;
            FootprintWidth = footprintWidth;
            FootprintDepth = footprintDepth;
            MinCell = minCell;
            DoorCell = doorCell;
        }

        public bool CoversCell(CellCoord cell)
        {
            return cell.X >= MinCell.X && cell.X < MinCell.X + FootprintWidth &&
                   cell.Z >= MinCell.Z && cell.Z < MinCell.Z + FootprintDepth;
        }

        /// <summary>True for every footprint cell except the door.</summary>
        public bool BlocksCell(CellCoord cell)
        {
            return CoversCell(cell) && cell != DoorCell;
        }
    }
}
