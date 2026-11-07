using System;
using System.Collections.Generic;
using NUnit.Framework;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;

namespace TheEmptying.Tests.EditMode.Events
{
    public class EventBusTests
    {
        private readonly struct TestEvent : ISimEvent
        {
            public const string DefinitionIdValue = "test.event";

            public int Value { get; }

            public TestEvent(int value)
            {
                Value = value;
            }

            public string DefinitionId => DefinitionIdValue;
        }

        private sealed class AttributedTestException : Exception, IAttributedFailure
        {
            public AttributedTestException(string message) : base(message)
            {
            }
        }

        private static (EventDefinitionRegistry definitions, EventBus bus) CreateBus(params string[] extraDefinitionIds)
        {
            var definitions = new EventDefinitionRegistry();
            definitions.Register(new EventDefinition(TestEvent.DefinitionIdValue, EventVisibility.Public, 0f, 0f, "test"));
            foreach (string id in extraDefinitionIds)
            {
                definitions.Register(new EventDefinition(id, EventVisibility.Public, 0f, 0f, "test"));
            }

            var bus = new EventBus(definitions);
            return (definitions, bus);
        }

        [Test]
        public void Events_AreDeliveredInPostOrder_OnlyAtDeliverPending()
        {
            (_, EventBus bus) = CreateBus();
            var received = new List<int>();
            bus.Subscribe<TestEvent>(e => received.Add(e.Value));

            bus.Post(new TestEvent(1), 0);
            bus.Post(new TestEvent(2), 0);
            bus.Post(new TestEvent(3), 0);

            CollectionAssert.IsEmpty(received);

            bus.DeliverPending();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
        }

        [Test]
        public void EventPostedDuringDelivery_ArrivesNextDelivery_NotCurrentOne()
        {
            (_, EventBus bus) = CreateBus();
            var received = new List<int>();

            bus.Subscribe<TestEvent>(e =>
            {
                received.Add(e.Value);
                if (e.Value == 1)
                {
                    bus.Post(new TestEvent(99), 0);
                }
            });

            bus.Post(new TestEvent(1), 0);
            bus.DeliverPending();

            CollectionAssert.AreEqual(new[] { 1 }, received);

            bus.DeliverPending();

            CollectionAssert.AreEqual(new[] { 1, 99 }, received);
        }

        [Test]
        public void SubscribeByType_And_ByDefinitionId_BothReceiveTypedEvent()
        {
            (_, EventBus bus) = CreateBus();
            int byType = -1;
            string byId = null;

            bus.Subscribe<TestEvent>(e => byType = e.Value);
            bus.Subscribe(TestEvent.DefinitionIdValue, posted => byId = posted.TypedEvent is TestEvent te ? $"typed:{te.Value}" : "not-typed");

            bus.Post(new TestEvent(42), 0);
            bus.DeliverPending();

            Assert.AreEqual(42, byType);
            Assert.AreEqual("typed:42", byId);
        }

        [Test]
        public void SubscribeByDefinitionId_ReceivesDataEvent_WithParametersIntact()
        {
            const string dataEventId = "test.data_event";
            (_, EventBus bus) = CreateBus(dataEventId);

            PostedEvent received = null;
            bus.Subscribe(dataEventId, posted => received = posted);

            var someEntity = new EntityId(5, 1);
            var data = new EventData()
                .SetEntity("actor", someEntity)
                .SetLong("amount", 7L)
                .SetDouble("severity", 2.5)
                .SetBool("witnessed", true)
                .SetString("note", "hello");

            bus.Post(dataEventId, 0, data);
            bus.DeliverPending();

            Assert.IsNotNull(received);
            Assert.IsFalse(received.IsTyped);
            Assert.AreEqual(someEntity, received.Data.GetEntity("actor"));
            Assert.AreEqual(7L, received.Data.GetLong("amount"));
            Assert.AreEqual(2.5, received.Data.GetDouble("severity"));
            Assert.IsTrue(received.Data.GetBool("witnessed"));
            Assert.AreEqual("hello", received.Data.GetString("note"));
        }

        [Test]
        public void Unsubscribing_StopsDelivery()
        {
            (_, EventBus bus) = CreateBus();
            var received = new List<int>();
            EventSubscriptionToken token = bus.Subscribe<TestEvent>(e => received.Add(e.Value));

            bus.Post(new TestEvent(1), 0);
            bus.DeliverPending();

            token.Unsubscribe();

            bus.Post(new TestEvent(2), 0);
            bus.DeliverPending();

            CollectionAssert.AreEqual(new[] { 1 }, received);
        }

        [Test]
        public void PostingUnregisteredDefinitionId_Throws()
        {
            (_, EventBus bus) = CreateBus();
            Assert.Throws<InvalidOperationException>(() => bus.Post("event.not_registered", 0));
        }

        [Test]
        public void ThrowingSubscriber_ProducesErrorNamingEventAndSubscriber()
        {
            (_, EventBus bus) = CreateBus();
            bus.Subscribe<TestEvent>(e => throw new Exception("boom"));

            bus.Post(new TestEvent(1), 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => bus.DeliverPending());
            StringAssert.Contains(TestEvent.DefinitionIdValue, ex.Message);
            Assert.IsNotNull(ex.InnerException);
            Assert.AreEqual("boom", ex.InnerException.Message);
        }

        [Test]
        public void AttributedFailure_IsRethrownUnwrapped_NotWrappedAgain()
        {
            (_, EventBus bus) = CreateBus();
            bus.Subscribe<TestEvent>(e => throw new AttributedTestException("already-attributed"));

            bus.Post(new TestEvent(1), 0);

            AttributedTestException ex = Assert.Throws<AttributedTestException>(() => bus.DeliverPending());
            Assert.AreEqual("already-attributed", ex.Message);
        }
    }
}
