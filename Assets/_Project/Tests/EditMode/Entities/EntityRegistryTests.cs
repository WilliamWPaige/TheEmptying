using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TheEmptying.Core.Entities;

namespace TheEmptying.Tests.EditMode.Entities
{
    public class EntityRegistryTests
    {
        [Test]
        public void CreateDestroyReuse_ReusedIndexHasHigherGeneration_OldIdRejected()
        {
            var registry = new EntityRegistry();
            EntityId first = registry.Create(EntityKind.Person);

            registry.Destroy(first);
            Assert.IsFalse(registry.IsAlive(first));

            EntityId reused = registry.Create(EntityKind.Person);

            Assert.AreEqual(first.Index, reused.Index);
            Assert.Greater(reused.Generation, first.Generation);
            Assert.IsFalse(registry.IsAlive(first));
            Assert.IsTrue(registry.IsAlive(reused));

            Assert.Throws<InvalidOperationException>(() => registry.Destroy(first));
            Assert.Throws<InvalidOperationException>(() => registry.GetKind(first));
        }

        [Test]
        public void FreedIndices_AreReusedFirstFreedFirstReused()
        {
            var registry = new EntityRegistry();
            EntityId a = registry.Create(EntityKind.Person);
            EntityId b = registry.Create(EntityKind.Building);
            registry.Create(EntityKind.Item);

            registry.Destroy(a);
            registry.Destroy(b);

            EntityId reusedFirst = registry.Create(EntityKind.Person);
            EntityId reusedSecond = registry.Create(EntityKind.Person);

            Assert.AreEqual(a.Index, reusedFirst.Index);
            Assert.AreEqual(b.Index, reusedSecond.Index);
        }

        [Test]
        public void Destroy_RemovesEntityRowsFromEveryRegisteredTable()
        {
            var registry = new EntityRegistry();
            var hunger = new EntityTable<float>(registry);
            var name = new EntityTable<string>(registry);

            EntityId id = registry.Create(EntityKind.Person);
            hunger.Add(id, 0.5f);
            name.Add(id, "test");

            registry.Destroy(id);

            Assert.IsFalse(hunger.Has(id));
            Assert.IsFalse(name.Has(id));
        }

        [Test]
        public void ExportAndRestoreState_NextCreateMatchesWhatItWouldHaveBeen()
        {
            var original = new EntityRegistry();
            EntityId a = original.Create(EntityKind.Person);
            original.Create(EntityKind.Building);
            original.Destroy(a);
            original.Create(EntityKind.Item);

            EntityRegistryState state = original.ExportState();

            EntityId expectedNext = original.Create(EntityKind.Group);

            var restored = new EntityRegistry();
            restored.RestoreState(state);
            EntityId actualNext = restored.Create(EntityKind.Group);

            Assert.AreEqual(expectedNext.Index, actualNext.Index);
            Assert.AreEqual(expectedNext.Generation, actualNext.Generation);
        }

        [Test]
        public void LivingEntities_IteratesInAscendingIndexOrder()
        {
            var registry = new EntityRegistry();
            var ids = new List<EntityId>();
            for (int i = 0; i < 10; i++)
            {
                ids.Add(registry.Create(EntityKind.Item));
            }

            registry.Destroy(ids[5]);
            registry.Destroy(ids[2]);
            registry.Create(EntityKind.Item);
            registry.Create(EntityKind.Item);

            var indices = registry.LivingEntities().Select(id => id.Index).ToList();
            var sorted = new List<int>(indices);
            sorted.Sort();

            CollectionAssert.AreEqual(sorted, indices);
        }
    }
}
