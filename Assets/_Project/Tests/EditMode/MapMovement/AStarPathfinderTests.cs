using System.Linq;
using NUnit.Framework;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.MapMovement.Pathfinding;

namespace TheEmptying.Tests.EditMode.MapMovement
{
    public class AStarPathfinderTests
    {
        private static MapGrid OpenGrid(int size = 10) => new MapGrid(size, size, 3.0);

        [Test]
        public void FindPath_OpenGrid_UsesDiagonalShortcut()
        {
            MapGrid grid = OpenGrid();
            PathResult result = AStarPathfinder.FindPath(grid, new GridPoint(0, 0), new GridPoint(3, 3));

            Assert.IsTrue(result.Found);
            // A pure diagonal run is 4 cells (start + 3 diagonal steps), strictly
            // shorter than any path that ever moves orthogonally for this case.
            Assert.AreEqual(4, result.Cells.Count);
            Assert.AreEqual(new GridPoint(0, 0), result.Cells[0]);
            Assert.AreEqual(new GridPoint(3, 3), result.Cells[^1]);
        }

        [Test]
        public void FindPath_AvoidsABlockedWall()
        {
            MapGrid grid = OpenGrid();
            for (int y = 0; y < 8; y++)
            {
                grid.SetWalkable(new GridPoint(5, y), false);
            }

            PathResult result = AStarPathfinder.FindPath(grid, new GridPoint(0, 0), new GridPoint(9, 0));

            Assert.IsTrue(result.Found);
            Assert.IsTrue(result.Cells.All(c => grid.IsWalkable(c)));
        }

        [Test]
        public void FindPath_DoesNotCutACornerPastABlockedCell()
        {
            // Block the two orthogonal cells between start and goal, leaving only
            // the direct diagonal step passable: cutting that corner must be refused.
            MapGrid grid = OpenGrid(6);
            grid.SetWalkable(new GridPoint(3, 2), false);
            grid.SetWalkable(new GridPoint(2, 3), false);

            PathResult result = AStarPathfinder.FindPath(grid, new GridPoint(2, 2), new GridPoint(3, 3));

            Assert.IsTrue(result.Found);
            Assert.Greater(result.Cells.Count, 2, "A direct diagonal step would cut the corner between the two blocked cells.");
        }

        [Test]
        public void FindPath_NoPathExists_ReturnsNotFoundRatherThanThrowing()
        {
            MapGrid grid = OpenGrid();
            for (int y = 0; y < grid.Depth; y++)
            {
                grid.SetWalkable(new GridPoint(5, y), false);
            }

            PathResult result = AStarPathfinder.FindPath(grid, new GridPoint(0, 0), new GridPoint(9, 0));

            Assert.IsFalse(result.Found);
            Assert.AreEqual(0, result.Cells.Count);
        }

        [Test]
        public void FindPath_UnwalkableGoal_ReturnsNoPath()
        {
            MapGrid grid = OpenGrid();
            grid.SetWalkable(new GridPoint(5, 5), false);

            PathResult result = AStarPathfinder.FindPath(grid, new GridPoint(0, 0), new GridPoint(5, 5));

            Assert.IsFalse(result.Found);
        }

        [Test]
        public void FindPath_StartEqualsGoal_ReturnsASingleCellPath()
        {
            MapGrid grid = OpenGrid();
            PathResult result = AStarPathfinder.FindPath(grid, new GridPoint(4, 4), new GridPoint(4, 4));

            Assert.IsTrue(result.Found);
            CollectionAssert.AreEqual(new[] { new GridPoint(4, 4) }, result.Cells);
        }

        [Test]
        public void FindPath_SameInputTwice_ReturnsTheExactSamePath()
        {
            MapGrid grid = OpenGrid(20);
            for (int y = 2; y < 15; y++)
            {
                grid.SetWalkable(new GridPoint(10, y), false);
            }

            PathResult first = AStarPathfinder.FindPath(grid, new GridPoint(0, 8), new GridPoint(19, 8));
            PathResult second = AStarPathfinder.FindPath(grid, new GridPoint(0, 8), new GridPoint(19, 8));

            Assert.IsTrue(first.Found);
            CollectionAssert.AreEqual(first.Cells, second.Cells);
        }

        [Test]
        public void FindPath_WithHigherCostTerrain_PrefersTheCheaperRoute()
        {
            MapGrid grid = OpenGrid(5);
            // The direct route along row 0 passes through (2, 0); make it very
            // expensive so the optimal path detours around it instead.
            grid.SetMovementCost(new GridPoint(2, 0), 100f);

            PathResult result = AStarPathfinder.FindPath(grid, new GridPoint(0, 0), new GridPoint(4, 0));

            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.Cells.Contains(new GridPoint(2, 0)), "The expensive cell should be routed around.");
        }
    }
}
