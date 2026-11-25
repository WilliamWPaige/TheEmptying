using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Modules.Map;
using TheEmptying.Presentation.Zones;

namespace TheEmptying.Tests.EditMode.Presentation
{
    /// <summary>R18b: the zoning menu's Undo stack - one snapshot per stroke, capped at a maximum depth, with Erase all counting as a stroke too.</summary>
    public class ZonePaintUndoStackTests
    {
        private static HashSet<CellCoord> Cells(params int[] xs)
        {
            var set = new HashSet<CellCoord>();
            foreach (int x in xs)
            {
                set.Add(new CellCoord(x, 0));
            }

            return set;
        }

        [Test]
        public void TryUndo_WithNothingPushed_ReturnsFalse()
        {
            var stack = new ZonePaintUndoStack(20);
            Assert.IsFalse(stack.TryUndo(out _));
        }

        [Test]
        public void Push_ThenUndo_RestoresTheSnapshotBeforeThatStroke()
        {
            var stack = new ZonePaintUndoStack(20);

            stack.Push(Cells(1)); // snapshot before painting cell 2
            stack.Push(Cells(1, 2)); // snapshot before painting cell 3

            Assert.IsTrue(stack.TryUndo(out HashSet<CellCoord> previous));
            CollectionAssert.AreEquivalent(Cells(1, 2), previous);

            Assert.IsTrue(stack.TryUndo(out HashSet<CellCoord> firstPrevious));
            CollectionAssert.AreEquivalent(Cells(1), firstPrevious);

            Assert.IsFalse(stack.TryUndo(out _), "Only two strokes were pushed.");
        }

        [Test]
        public void Push_PastMaxSteps_DropsTheOldestStep()
        {
            var stack = new ZonePaintUndoStack(20);

            for (int i = 0; i < 25; i++)
            {
                stack.Push(Cells(i));
            }

            Assert.AreEqual(20, stack.Count, "The depth never exceeds the cap.");

            // The oldest surviving snapshot should be from push #5 (i=5), since pushes 0-4 were dropped.
            HashSet<CellCoord> last = null;
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(stack.TryUndo(out last));
            }

            CollectionAssert.AreEquivalent(Cells(5), last);
            Assert.IsFalse(stack.TryUndo(out _));
        }

        [Test]
        public void EraseAll_CountsAsAStroke_UndoBringsThePaintBack()
        {
            var stack = new ZonePaintUndoStack(20);
            HashSet<CellCoord> paintedBeforeErase = Cells(1, 2, 3);

            // Erase All: push the current paint before clearing it.
            stack.Push(paintedBeforeErase);

            Assert.IsTrue(stack.TryUndo(out HashSet<CellCoord> restored));
            CollectionAssert.AreEquivalent(paintedBeforeErase, restored);
        }

        [Test]
        public void MaxSteps_IsAtLeastOne_EvenIfConstructedWithZeroOrNegative()
        {
            Assert.AreEqual(1, new ZonePaintUndoStack(0).MaxSteps);
            Assert.AreEqual(1, new ZonePaintUndoStack(-5).MaxSteps);
        }
    }
}
