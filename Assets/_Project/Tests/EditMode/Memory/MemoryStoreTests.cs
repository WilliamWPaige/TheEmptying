using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Memory;
using TheEmptying.Core.Saves;

namespace TheEmptying.Tests.EditMode.Memory
{
    public class MemoryStoreTests
    {
        [Test]
        public void SetThenGetFlag_RoundTrips()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId id = entities.Create(EntityKind.Person);

            store.SetFlag(id, "known", true);

            Assert.IsTrue(store.GetFlag(id, "known"));
        }

        [Test]
        public void GetFlag_NeverSet_DefaultsToFalse()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId id = entities.Create(EntityKind.Person);

            Assert.IsFalse(store.GetFlag(id, "unset"));
        }

        [Test]
        public void SetThenGetCounter_RoundTrips()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId id = entities.Create(EntityKind.Person);

            store.SetCounter(id, "gold", 12.5);

            Assert.AreEqual(12.5, store.GetCounter(id, "gold"));
        }

        [Test]
        public void GetCounter_NeverSet_DefaultsToZero()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId id = entities.Create(EntityKind.Person);

            Assert.AreEqual(0.0, store.GetCounter(id, "unset"));
        }

        [Test]
        public void ChangeCounter_AddsToTheCurrentValue_AndReturnsTheNewValue()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId id = entities.Create(EntityKind.Person);

            store.SetCounter(id, "gold", 10);
            double result = store.ChangeCounter(id, "gold", 5);

            Assert.AreEqual(15.0, result);
            Assert.AreEqual(15.0, store.GetCounter(id, "gold"));
        }

        [Test]
        public void ChangeCounter_NegativeAmount_Subtracts()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId id = entities.Create(EntityKind.Person);

            store.SetCounter(id, "gold", 10);
            store.ChangeCounter(id, "gold", -3);

            Assert.AreEqual(7.0, store.GetCounter(id, "gold"));
        }

        [Test]
        public void KingdomOwnedValues_UseEntityIdNone_AndAreIndependentOfEntities()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId person = entities.Create(EntityKind.Person);

            store.SetFlag(EntityId.None, "war", true);
            store.SetCounter(EntityId.None, "treasury", 100);

            Assert.IsTrue(store.GetFlag(EntityId.None, "war"));
            Assert.AreEqual(100.0, store.GetCounter(EntityId.None, "treasury"));
            Assert.IsFalse(store.GetFlag(person, "war"), "a person's own flag of the same name should be independent of the kingdom's");
        }

        [Test]
        public void DestroyingAnEntity_RemovesItsFlagsAndCounters_ButNotTheKingdoms()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId id = entities.Create(EntityKind.Person);

            store.SetFlag(id, "known", true);
            store.SetCounter(id, "visits", 3);
            store.SetCounter(EntityId.None, "treasury", 100);

            entities.Destroy(id);

            Assert.IsFalse(store.GetFlag(id, "known"));
            Assert.AreEqual(0.0, store.GetCounter(id, "visits"));
            Assert.IsFalse(store.Has(id));
            Assert.AreEqual(100.0, store.GetCounter(EntityId.None, "treasury"), "the kingdom's own counters must survive an unrelated entity's destruction");
        }

        [Test]
        public void AllFlagsAndCounters_AreOrderedByOwnerIndexThenNameOrdinal()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId a = entities.Create(EntityKind.Person);
            EntityId b = entities.Create(EntityKind.Person);

            store.SetFlag(b, "zebra", true);
            store.SetFlag(b, "apple", true);
            store.SetFlag(a, "mango", true);
            store.SetFlag(EntityId.None, "kingdomFlag", true);

            List<(EntityId Owner, string Name, bool Value)> ordered = store.AllFlags().ToList();

            CollectionAssert.AreEqual(
                new[] { (EntityId.None, "kingdomFlag"), (a, "mango"), (b, "apple"), (b, "zebra") },
                ordered.Select(e => (e.Owner, e.Name)).ToList());
        }

        [Test]
        public void SaveAndLoad_RoundTripsFlagsAndCounters()
        {
            var entities = new EntityRegistry();
            var store = new MemoryStore(entities);
            EntityId a = entities.Create(EntityKind.Person);
            EntityId b = entities.Create(EntityKind.Building);

            store.SetFlag(a, "known", true);
            store.SetFlag(b, "burned", false);
            store.SetCounter(a, "visits", 7);
            store.SetCounter(EntityId.None, "treasury", 250.5);

            byte[] data = MemorySection.Write(store);

            var loadedEntities = new EntityRegistry();
            loadedEntities.Create(EntityKind.Person); // reproduce the same indices
            loadedEntities.Create(EntityKind.Building);
            var loaded = new MemoryStore(loadedEntities);

            MemorySection.Read(data, loaded);

            Assert.IsTrue(loaded.GetFlag(a, "known"));
            Assert.IsFalse(loaded.GetFlag(b, "burned"));
            Assert.AreEqual(7.0, loaded.GetCounter(a, "visits"));
            Assert.AreEqual(250.5, loaded.GetCounter(EntityId.None, "treasury"));
        }

        [Test]
        public void Write_ProducesEntriesInStableOrder_RegardlessOfInsertionOrder()
        {
            var entitiesA = new EntityRegistry();
            var storeA = new MemoryStore(entitiesA);
            EntityId a1 = entitiesA.Create(EntityKind.Person);
            EntityId a2 = entitiesA.Create(EntityKind.Person);
            storeA.SetFlag(a2, "zebra", true);
            storeA.SetFlag(a1, "mango", true);
            storeA.SetFlag(a2, "apple", true);

            var entitiesB = new EntityRegistry();
            var storeB = new MemoryStore(entitiesB);
            EntityId b1 = entitiesB.Create(EntityKind.Person);
            EntityId b2 = entitiesB.Create(EntityKind.Person);
            storeB.SetFlag(b1, "mango", true);
            storeB.SetFlag(b2, "apple", true);
            storeB.SetFlag(b2, "zebra", true);

            CollectionAssert.AreEqual(MemorySection.Write(storeA), MemorySection.Write(storeB), "the same logical content written in a different order should serialize identically.");
        }
    }
}
