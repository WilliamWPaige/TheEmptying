using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Randomness;

namespace TheEmptying.Tests.EditMode.Randomness
{
    public class SimRandomTests
    {
        [Test]
        public void SameSeedAndSequence_GivesSameSequence()
        {
            var a = SimRandom.CreateSeeded(12345, 1);
            var b = SimRandom.CreateSeeded(12345, 1);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void DifferentSequence_GivesDifferentSequence()
        {
            var a = SimRandom.CreateSeeded(12345, 1);
            var b = SimRandom.CreateSeeded(12345, 2);

            bool differs = false;
            for (int i = 0; i < 20; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    differs = true;
                    break;
                }
            }

            Assert.IsTrue(differs);
        }

        [Test]
        public void ExportAndRestoreState_ContinuesExactSequence()
        {
            var rng = SimRandom.CreateSeeded(999, 7);
            rng.NextUInt();
            rng.NextUInt();
            rng.NextUInt();

            SimRandomState state = rng.GetState();
            uint expectedNext = rng.NextUInt();

            var restored = SimRandom.FromState(state);
            uint actualNext = restored.NextUInt();

            Assert.AreEqual(expectedNext, actualNext);
        }

        [Test]
        public void WeightedPick_MatchesWeightsWithinOnePercent_Over100000Rolls()
        {
            var rng = SimRandom.CreateSeeded(42, 1);
            float[] weights = { 1f, 2f, 3f, 4f };
            float totalWeight = 10f;
            const int rolls = 100000;
            var counts = new int[weights.Length];

            for (int i = 0; i < rolls; i++)
            {
                counts[rng.WeightedPick(weights)]++;
            }

            for (int i = 0; i < weights.Length; i++)
            {
                float expectedFraction = weights[i] / totalWeight;
                float actualFraction = counts[i] / (float)rolls;
                Assert.That(actualFraction, Is.EqualTo(expectedFraction).Within(0.01f));
            }
        }

        [Test]
        public void Shuffle_SameSeed_GivesSameOrder()
        {
            var listA = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var listB = new List<int>(listA);

            SimRandom.CreateSeeded(555, 3).Shuffle(listA);
            SimRandom.CreateSeeded(555, 3).Shuffle(listB);

            CollectionAssert.AreEqual(listA, listB);
        }
    }
}
