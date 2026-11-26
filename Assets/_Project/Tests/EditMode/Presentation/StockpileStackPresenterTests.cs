using NUnit.Framework;
using TheEmptying.Presentation.Zones;

namespace TheEmptying.Tests.EditMode.Presentation
{
    /// <summary>R20: a stack's placeholder visual stage - 1 from any amount, 2 from a third of capacity, 3 from two thirds, dropping back as it empties.</summary>
    public class StockpileStackPresenterTests
    {
        [TestCase(0, 100, 0)]
        [TestCase(1, 100, 1)]
        [TestCase(32, 100, 1)]
        [TestCase(33, 100, 1)]
        [TestCase(34, 100, 2)]
        [TestCase(65, 100, 2)]
        [TestCase(66, 100, 2)]
        [TestCase(67, 100, 3)]
        [TestCase(100, 100, 3)]
        public void StageFor_UsesOneAndTwoThirdsOfCapacityAsThresholds(int amountStored, int capacityPerSpot, int expectedStage)
        {
            Assert.AreEqual(expectedStage, StockpileStackPresenter.StageFor(amountStored, capacityPerSpot));
        }
    }
}
