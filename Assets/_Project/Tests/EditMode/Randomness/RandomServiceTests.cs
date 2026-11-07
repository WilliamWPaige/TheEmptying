using NUnit.Framework;
using TheEmptying.Core.Randomness;

namespace TheEmptying.Tests.EditMode.Randomness
{
    public class RandomServiceTests
    {
        [Test]
        public void SameWorldSeedAndStreamName_GivesSameSequence()
        {
            var serviceA = new RandomService(123);
            var serviceB = new RandomService(123);

            SimRandom streamA = serviceA.GetStream("needs");
            SimRandom streamB = serviceB.GetStream("needs");

            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(streamA.NextUInt(), streamB.NextUInt());
            }
        }

        [Test]
        public void DifferentStreamNames_GiveDifferentSequences()
        {
            var service = new RandomService(123);
            SimRandom needs = service.GetStream("needs");
            SimRandom events = service.GetStream("events");

            bool differs = false;
            for (int i = 0; i < 20; i++)
            {
                if (needs.NextUInt() != events.NextUInt())
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
            var service = new RandomService(321);
            SimRandom stream = service.GetStream("needs");
            stream.NextUInt();
            stream.NextUInt();

            RandomServiceState state = service.ExportState();
            uint expectedNext = service.GetStream("needs").NextUInt();

            var restoredService = new RandomService(321);
            restoredService.RestoreState(state);
            uint actualNext = restoredService.GetStream("needs").NextUInt();

            Assert.AreEqual(expectedNext, actualNext);
        }
    }
}
