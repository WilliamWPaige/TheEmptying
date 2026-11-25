using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation.Zones;

namespace TheEmptying.Tests.EditMode.Presentation
{
    /// <summary>R18b: the brush's own cell footprint, line-filling a dragged stroke without gaps, and the "does an NxN square fit" rule that gates Create.</summary>
    public class ZonePaintGeometryTests
    {
        [Test]
        public void BrushFootprint_Square_IsAnchoredAtItsMinCorner()
        {
            var cells = new HashSet<CellCoord>(ZonePaintGeometry.BrushFootprint(new CellCoord(10, 10), BrushShape.Square, 2));

            Assert.AreEqual(4, cells.Count);
            Assert.IsTrue(cells.Contains(new CellCoord(10, 10)));
            Assert.IsTrue(cells.Contains(new CellCoord(11, 10)));
            Assert.IsTrue(cells.Contains(new CellCoord(10, 11)));
            Assert.IsTrue(cells.Contains(new CellCoord(11, 11)));
        }

        [Test]
        public void BrushFootprint_Round_IsCenteredOnTheAnchorCell()
        {
            var cells = new HashSet<CellCoord>(ZonePaintGeometry.BrushFootprint(new CellCoord(0, 0), BrushShape.Round, 4));

            Assert.IsTrue(cells.Contains(new CellCoord(0, 0)), "The center cell must always be included.");
            Assert.IsFalse(cells.Contains(new CellCoord(10, 10)), "A round brush must not reach far-away cells.");
        }

        [Test]
        public void LineCells_BetweenTwoFarApartCells_HasNoGaps()
        {
            var cells = new List<CellCoord>(ZonePaintGeometry.LineCells(new CellCoord(0, 0), new CellCoord(5, 2)));

            Assert.AreEqual(new CellCoord(0, 0), cells[0]);
            Assert.AreEqual(new CellCoord(5, 2), cells[cells.Count - 1]);

            // Every consecutive pair must be an immediate (8-way) neighbor - no jump leaves a gap.
            for (int i = 1; i < cells.Count; i++)
            {
                int dx = System.Math.Abs(cells[i].X - cells[i - 1].X);
                int dz = System.Math.Abs(cells[i].Z - cells[i - 1].Z);
                Assert.LessOrEqual(dx, 1);
                Assert.LessOrEqual(dz, 1);
            }
        }

        [Test]
        public void BrushStroke_FastDragWithALargeGapBetweenFrames_LeavesNoGaps()
        {
            // Simulates a single Update() tick where the mouse jumped several cells (moving fast) with a size-1 brush.
            var cells = new HashSet<CellCoord>(ZonePaintGeometry.BrushStroke(new CellCoord(0, 0), new CellCoord(10, 0), BrushShape.Square, sizeCells: 1));

            for (int x = 0; x <= 10; x++)
            {
                Assert.IsTrue(cells.Contains(new CellCoord(x, 0)), $"Cell ({x},0) along the dragged line must be painted.");
            }
        }

        [Test]
        public void ContainsSquare_TrueWhenASolidBlockOfThatSizeExists()
        {
            var cells = new HashSet<CellCoord>
            {
                new CellCoord(0, 0), new CellCoord(1, 0),
                new CellCoord(0, 1), new CellCoord(1, 1)
            };

            Assert.IsTrue(ZonePaintGeometry.ContainsSquare(cells, 2));
        }

        [Test]
        public void ContainsSquare_FalseWhenOnlyAnLShapeExists()
        {
            // 3 cells of an L - no 2x2 block is fully present.
            var cells = new HashSet<CellCoord>
            {
                new CellCoord(0, 0), new CellCoord(1, 0), new CellCoord(0, 1)
            };

            Assert.IsFalse(ZonePaintGeometry.ContainsSquare(cells, 2));
        }

        [Test]
        public void ContainsSquare_FalseWhenFewerCellsThanTheSquareNeeds()
        {
            var cells = new HashSet<CellCoord> { new CellCoord(0, 0) };
            Assert.IsFalse(ZonePaintGeometry.ContainsSquare(cells, 2));
        }
    }
}
