using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Scheduling;

namespace TheEmptying.Tests.EditMode.Scheduling
{
    public class SchedulerTests
    {
        private sealed class RecordingSystem : ISimSystem
        {
            public string Id { get; }
            public List<long> Calls { get; } = new List<long>();

            public RecordingSystem(string id)
            {
                Id = id;
            }

            public void Update(long tick)
            {
                Calls.Add(tick);
            }
        }

        private sealed class OrderRecordingSystem : ISimSystem
        {
            private readonly List<string> _log;

            public string Id { get; }

            public OrderRecordingSystem(string id, List<string> log)
            {
                Id = id;
                _log = log;
            }

            public void Update(long tick)
            {
                _log.Add(Id);
            }
        }

        [Test]
        public void Interval10Offset0_RunsAtTicks0_10_20()
        {
            var scheduler = new Scheduler();
            var system = new RecordingSystem("a");
            scheduler.Register(system, interval: 10, offset: 0, order: 0);

            for (long tick = 0; tick <= 20; tick++)
            {
                scheduler.RunTick(tick);
            }

            CollectionAssert.AreEqual(new long[] { 0, 10, 20 }, system.Calls);
        }

        [Test]
        public void Interval10Offset3_RunsAtTicks3_13_23()
        {
            var scheduler = new Scheduler();
            var system = new RecordingSystem("a");
            scheduler.Register(system, interval: 10, offset: 3, order: 0);

            for (long tick = 0; tick <= 23; tick++)
            {
                scheduler.RunTick(tick);
            }

            CollectionAssert.AreEqual(new long[] { 3, 13, 23 }, system.Calls);
        }

        [Test]
        public void SameInterval_DifferentOffsets_RunOnDifferentTicks()
        {
            var scheduler = new Scheduler();
            var systemA = new RecordingSystem("a");
            var systemB = new RecordingSystem("b");
            scheduler.Register(systemA, interval: 10, offset: 0, order: 0);
            scheduler.Register(systemB, interval: 10, offset: 5, order: 1);

            for (long tick = 0; tick < 20; tick++)
            {
                scheduler.RunTick(tick);
            }

            CollectionAssert.AreEqual(new long[] { 0, 10 }, systemA.Calls);
            CollectionAssert.AreEqual(new long[] { 5, 15 }, systemB.Calls);
        }

        [Test]
        public void RunOrder_FollowsOrderValueThenIdTiebreak()
        {
            var scheduler = new Scheduler();
            var executionOrder = new List<string>();

            var systemB = new OrderRecordingSystem("B", executionOrder);
            var systemA = new OrderRecordingSystem("A", executionOrder);
            var systemC = new OrderRecordingSystem("C", executionOrder);

            scheduler.Register(systemB, interval: 1, offset: 0, order: 5);
            scheduler.Register(systemA, interval: 1, offset: 0, order: 5);
            scheduler.Register(systemC, interval: 1, offset: 0, order: 1);

            scheduler.RunTick(0);

            CollectionAssert.AreEqual(new[] { "C", "A", "B" }, executionOrder);
        }

        [Test]
        public void RunOrder_StaysStableWhenSystemsAreAddedLater()
        {
            var scheduler = new Scheduler();
            var executionOrder = new List<string>();

            var systemA = new OrderRecordingSystem("A", executionOrder);
            var systemB = new OrderRecordingSystem("B", executionOrder);
            scheduler.Register(systemA, interval: 1, offset: 0, order: 1);
            scheduler.Register(systemB, interval: 1, offset: 0, order: 2);

            scheduler.RunTick(0);
            CollectionAssert.AreEqual(new[] { "A", "B" }, executionOrder);

            executionOrder.Clear();
            var systemZ = new OrderRecordingSystem("Z", executionOrder);
            scheduler.Register(systemZ, interval: 1, offset: 0, order: 0);

            scheduler.RunTick(1);
            CollectionAssert.AreEqual(new[] { "Z", "A", "B" }, executionOrder);
        }
    }
}
