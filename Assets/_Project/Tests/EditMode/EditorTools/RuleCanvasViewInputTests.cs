using NUnit.Framework;
using TheEmptying.Editor.Rules.Canvas.UI;

namespace TheEmptying.Tests.EditMode.EditorTools
{
    /// <summary>Prompt 2.6 housekeeping: Unity's UI Toolkit pointer button numbering (left=0, right=1, middle=2) and the pan trigger built from it.</summary>
    public class RuleCanvasViewInputTests
    {
        [Test]
        public void IsPanTrigger_MiddleButton_IsTrue()
        {
            Assert.IsTrue(RuleCanvasView.IsPanTrigger(RuleCanvasView.MiddleMouseButton, altKey: false));
        }

        [Test]
        public void IsPanTrigger_AltPlusLeftButton_IsTrue()
        {
            Assert.IsTrue(RuleCanvasView.IsPanTrigger(RuleCanvasView.LeftMouseButton, altKey: true));
        }

        [Test]
        public void IsPanTrigger_PlainLeftButton_IsFalse()
        {
            Assert.IsFalse(RuleCanvasView.IsPanTrigger(RuleCanvasView.LeftMouseButton, altKey: false));
        }

        [Test]
        public void IsPanTrigger_RightButton_IsFalse_EvenWithAlt()
        {
            Assert.IsFalse(RuleCanvasView.IsPanTrigger(RuleCanvasView.RightMouseButton, altKey: false));
            Assert.IsFalse(RuleCanvasView.IsPanTrigger(RuleCanvasView.RightMouseButton, altKey: true));
        }
    }
}
