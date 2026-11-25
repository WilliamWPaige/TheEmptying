using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Presentation.Zones;

namespace TheEmptying.Tests.EditMode.Presentation
{
    /// <summary>R18: the zoning menu's default-name rule - "Stockpile Zone", "Stockpile Zone 2", "Stockpile Zone 3", and so on.</summary>
    public class ZoneNamingTests
    {
        [Test]
        public void NextDefaultName_WhenBaseNameIsFree_ReturnsBaseName()
        {
            string result = ZoneNaming.NextDefaultName("Stockpile Zone", new List<string>());
            Assert.AreEqual("Stockpile Zone", result);
        }

        [Test]
        public void NextDefaultName_WhenBaseNameIsTaken_ReturnsNumberTwo()
        {
            string result = ZoneNaming.NextDefaultName("Stockpile Zone", new List<string> { "Stockpile Zone" });
            Assert.AreEqual("Stockpile Zone 2", result);
        }

        [Test]
        public void NextDefaultName_CountsUpPastSeveralTakenNames()
        {
            var existing = new List<string> { "Stockpile Zone", "Stockpile Zone 2", "Stockpile Zone 3" };
            string result = ZoneNaming.NextDefaultName("Stockpile Zone", existing);
            Assert.AreEqual("Stockpile Zone 4", result);
        }

        [Test]
        public void NextDefaultName_SkipsOnlyTakenNumbers_NotEveryGapAfterThem()
        {
            // "Stockpile Zone 2" is free even though "Stockpile Zone" and "Stockpile Zone 3" are taken.
            var existing = new List<string> { "Stockpile Zone", "Stockpile Zone 3" };
            string result = ZoneNaming.NextDefaultName("Stockpile Zone", existing);
            Assert.AreEqual("Stockpile Zone 2", result);
        }

        [Test]
        public void NextDefaultName_IsCaseSensitive_OrdinalComparison()
        {
            // Determinism rule: ordinal, never culture-sensitive comparison.
            string result = ZoneNaming.NextDefaultName("Stockpile Zone", new List<string> { "stockpile zone" });
            Assert.AreEqual("Stockpile Zone", result);
        }

        [Test]
        public void NextDefaultName_DifferentBaseName_IsUnaffectedByOtherNamesTaken()
        {
            var existing = new List<string> { "Stockpile Zone", "Stockpile Zone 2" };
            string result = ZoneNaming.NextDefaultName("Living Area Zone", existing);
            Assert.AreEqual("Living Area Zone", result);
        }
    }
}
